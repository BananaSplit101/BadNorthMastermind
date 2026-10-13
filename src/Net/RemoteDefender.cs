using System.Collections.Generic;
using BadNorthMP.Spectator;
using BadNorthMP.UI;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;
namespace BadNorthMP.Net
{
    // The campaign leader executes orders, but Steam identity determines squad ownership.
    internal class RemoteDefender : MonoBehaviour
    {
        public static EnglishSquad Selected { get; private set; }
        // When each order still waiting for the leader's reply was sent, oldest first (replies arrive in order).
        private static readonly Queue<float> pendingOrders = new Queue<float>();
        private static string lastRefusal;
        private static float nextRefusal;

        public static void Init()
        {
            Router.On(MsgType.DefenderMove, (sender, reader) => Receive(sender, reader, false));
            Router.On(MsgType.DefenderAction, (sender, reader) => Receive(sender, reader, true));
            Router.On(MsgType.DefenderOrderResult, (sender, reader) =>
            {
                if (sender != Roles.DefenderId) return;
                bool ok = reader.ReadBoolean();
                string message = reader.ReadString();
                string latency = pendingOrders.Count > 0 ? " after " + Mathf.RoundToInt((Time.unscaledTime - pendingOrders.Dequeue()) * 1000f) + " ms" : "";
                ModLog.Info("Order " + (ok ? "accepted" : "refused") + latency + ": " + message);
                if (ok) return;
                GameNotify.Post(message);
                NativeDefenderControls.RestoreTiles();
            });
        }

        /// <summary>Leader side: every order gets a reply, so a refused order is never silent on the sender's screen.</summary>
        private static void Receive(Steamworks.CSteamID sender, System.IO.BinaryReader reader, bool action)
        {
            var session = SteamSession.Instance;
            if (!Roles.IsDefender || sender == Roles.MastermindId || sender == session.Me || !session.Members().Contains(sender)) return;
            int token = reader.ReadInt32(), round = reader.ReadInt32(), seed = reader.ReadInt32();
            int level = reader.ReadInt32(), hero = reader.ReadInt32(), target = reader.ReadInt32();
            var manager = Singleton<IslandGameplayManager>.instance;
            var island = GameAccess.CurrentIsland;
            bool ok = false;
            string result;
            if (!PartyFlow.Deployed)
                result = "The party campaign isn't running.";
            else if (!GameAccess.IsPlaying || !manager || (!manager.states.Prepare.active && !manager.states.running.active))
                result = "Orders only work while the battle is on.";
            else if (token != PartyFlow.State.token || seed != Profile.campaign.seed)
                result = "That order was for a different campaign.";
            else if (level != island.levelNode.index)
                result = "That order was for a different island.";
            else if (round != PartyFlow.State.round)
            {
                // The sender missed the latest ownership update. Resend it so their next order works.
                session.Send(sender, Packet.Build(MsgType.PartyOwnership, PartyFlow.State.Write));
                result = "Squad ownership was out of date. Try the order again.";
            }
            else if (!PartyFlow.State.Owns(sender.m_SteamID, hero))
                result = "That commander isn't yours.";
            else
            {
                EnglishSquad squad = null;
                foreach (var candidate in OwnedSquads(sender))
                    if (Profile.campaign.heroes.IndexOf(candidate.hero) == hero) { squad = candidate; break; }
                if (!squad || !squad.selectable)
                    result = "Your squad can't take orders right now.";
                else
                {
                    PartyFlow.ExecutingOrder = true;
                    try { ok = action && target == 2 ? NativeDefenderControls.Execute(squad, reader, out result) : Execute(squad, target, action, out result); }
                    finally { PartyFlow.ExecutingOrder = false; }
                }
            }
            ModLog.Info("Order from " + SteamSession.NameOf(sender) + ": " + (action ? "action" : "move") +
                " commander " + hero + " target " + target + " -> " + result);
            session.Send(sender, Packet.Build(MsgType.DefenderOrderResult, w => { w.Write(ok); w.Write(result); }));
        }

        public static List<EnglishSquad> OwnedSquads(Steamworks.CSteamID player)
        {
            var result = new List<EnglishSquad>();
            foreach (var candidate in GameAccess.PlayerSquads)
            {
                var squad = candidate as EnglishSquad;
                if (squad && PartyFlow.Owns(player, squad.hero)) result.Add(squad);
            }
            result.Sort((a, b) => a.hero.id.CompareTo(b.hero.id));
            return result;
        }

        public static void Select(EnglishSquad squad)
        {
            if (!squad || !PartyFlow.CanSelect(squad)) return;
            Selected = squad;
            // The native selector moves the selection ring and opens the squad's banner.
            var selector = Singleton<SquadSelector>.instance;
            if (selector && selector.selectedSquad != squad) selector.SelectSquad(squad, false);
        }

        /// <summary>Remote defender: forward a native move order to the leader (deselect: like SquadMover.MoveTo).</summary>
        public static void Move(EnglishSquad squad, NavSpot target, bool deselect = false)
        {
            if (!squad || !target) return;
            if (!PartyFlow.CanOrder(squad, target)) { Refuse(squad); return; }
            Select(squad);
            if (Command(squad, GameAccess.CurrentIsland.navSpotter.navSpots.IndexOf(target), false))
                NativeDefenderControls.MoveSent(squad, target, deselect);
        }

        /// <summary>Feedback for an order this player isn't allowed to give, instead of silently ignoring the click.</summary>
        public static void Refuse(EnglishSquad squad) =>
            Refused(squad && !PartyFlow.CanSelect(squad) ? "That squad belongs to a teammate." : "That tile belongs to a teammate. Choose an empty tile.");

        public static void Refused(string message)
        {
            if (Roles.IsMastermind) return;
            if (message == lastRefusal && Time.unscaledTime < nextRefusal) return;
            lastRefusal = message;
            nextRefusal = Time.unscaledTime + 2f;
            ModLog.Info("Order refused here: " + message);
            GameNotify.Post(message);
        }

        /// <summary>Remote defenders: called by the ability patches when an order reaches the leader.</summary>
        public static void OrderSent(string description)
        {
            pendingOrders.Enqueue(Time.unscaledTime);
            ModLog.Info("Order sent: " + description);
        }

        public static bool Command(EnglishSquad squad, int target, bool action)
        {
            if (!squad || !PartyFlow.Deployed || !PartyFlow.CanSelect(squad))
            {
                ModLog.Info("Order not sent: no squad of yours is selected");
                return false;
            }
            if (Roles.IsDefender)
            {
                Execute(squad, target, action, out string result);
                GameNotify.Post(result);
                return true;
            }
            var island = GameAccess.CurrentIsland;
            if (!island || !SpectatorSession.IslandReady())
            {
                ModLog.Info("Order not sent: the battle is still loading");
                return false;
            }
            SteamSession.Instance.Send(Roles.DefenderId, Packet.Build(action ? MsgType.DefenderAction : MsgType.DefenderMove, w =>
            {
                w.Write(PartyFlow.State.token); w.Write(PartyFlow.State.round); w.Write(Profile.campaign.seed);
                w.Write((int)island.levelNode.index); w.Write(Profile.campaign.heroes.IndexOf(squad.hero)); w.Write(target);
            }));
            OrderSent((action ? "action " : "move ") + squad.hero.dbgName + " target " + target);
            return true;
        }

        private static bool Execute(EnglishSquad squad, int target, bool action, out string result)
        {
            var island = GameAccess.CurrentIsland;
            result = "Unknown squad command.";
            if (!squad.selectable) { result = "That commander is unavailable."; return false; }
            if (!action)
            {
                if (target < 0 || target >= island.navSpotter.navSpots.Count || !squad.navSpotOccupant.canMove) { result = "Your squad can't move right now."; return false; }
                var spot = island.navSpotter.navSpots[target];
                if (!PartyFlow.CanOrder(squad, spot)) { result = "That tile belongs to a teammate. Choose an empty tile."; return false; }
                // The sender plays the move sounds on their own machine.
                SquadMover.MoveToDirect(squad, spot);
                result = "Move order accepted.";
                return true;
            }
            if (target == 0)
            {
                var ability = squad.upgradeManager.GetUpgrade<Voxels.TowerDefense.Upgrades.ReplenishAbility>();
                if (!ability || !ability.IsAvailable()) { result = "Healing is unavailable: your squad is full or no house is free."; return false; }
                SquadReplenishLocation best = null; float distance = float.MaxValue;
                foreach (var location in ability.locations)
                {
                    float next = (location.transform.position - squad.heroAgent.transform.position).sqrMagnitude;
                    if (location.available && next < distance) { best = location; distance = next; }
                }
                if (!best) { result = "No healing house is available."; return false; }
                ability.SelectLocation(best);
                result = "Going to the nearest available house to heal.";
                return true;
            }
            // The leader never has a teammate's squad selected, so the ability's own boat list is empty here.
            if (target == 1) return Game.Evacuation.Order(squad, null, out result);
            return false;
        }

        public static void ForgetSelection()
        { Selected = null; pendingOrders.Clear(); }

        private void Update()
        {
            bool remote = Roles.IsRemoteDefender && SpectatorSession.Active && SpectatorSession.IslandReady();
            if (!PartyFlow.Deployed || !GameAccess.CurrentIsland || (Roles.IsRemoteDefender && !remote))
            {
                if (Selected && SpectatorSession.Active) SelectionLifecycle.Clear();
                else { if (Selected) Selected.ShowSelection(false); ForgetSelection(); }
                return;
            }
            if (Roles.IsMastermind) return;
            if (remote)
            {
                var states = Singleton<IslandGameplayManager>.instance.states;
                // Not while the game's loadout screen is up for the party's choice of commanders.
                if (!states.running.active && !UI.NativeLoadout.Showing) states.running.SetActive(true);
                NativeDefenderControls.UpdateTiles();
                NativeDefenderControls.Report();
            }
            // The native selector, banners and cursor handle input for every defender.
            Selected = Singleton<SquadSelector>.instance.selectedSquad;
        }
    }
    // Prevent each participant's normal Play button from opening a separate local campaign.
    [HarmonyPatch(typeof(Voxels.TowerDefense.UI.MainMenu), "Play")]
    internal static class SharedCampaignPlayPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!SteamSession.Instance || !SteamSession.Instance.InLobby) return true;
            MultiplayerScreen.Open();
            __result = true;
            return false;
        }
    }
}
