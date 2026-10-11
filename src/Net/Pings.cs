using System.Collections.Generic;
using BadNorthMP.Spectator;
using Steamworks;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Defenders mark spots on the island for each other (UI.PingWheel: hold the middle mouse button and pick what it
    /// means, or just click it). Only the party's defenders send and see them, never the mastermind; in single player
    /// they're shown just here. Each lasts a few seconds; a player has at most three up at once, and a burst of them is
    /// cut short.
    /// </summary>
    internal static class Pings
    {
        public enum Kind : byte { Look, Attack, Move, Hold, Retreat }

        public sealed class Ping
        {
            public CSteamID from;
            public string name;
            public Kind kind;
            public Vector3 position;
            public Color colour;
            public float at;
        }

        public const float Duration = 5f;
        private const int PerPlayer = 3, PerWindow = 4;
        private const float Window = 3f;

        public static readonly List<Ping> Active = new List<Ping>();
        private static readonly Dictionary<ulong, List<float>> recent = new Dictionary<ulong, List<float>>();
        private static int shownLevel = -1;

        private static SteamSession Session => SteamSession.Instance;

        public static string Label(Kind kind)
        {
            switch (kind)
            {
                case Kind.Attack: return "Attack";
                case Kind.Move: return "Move here";
                case Kind.Hold: return "Hold";
                case Kind.Retreat: return "Fall back";
                default: return "Look";
            }
        }

        /// <summary>
        /// A player's colour: their main commander's (the first they drafted, or in single player the campaign's first),
        /// the colour of its banner, so everyone knows whose ping it is at a glance.
        /// </summary>
        public static Color Colour(CSteamID player)
        {
            var heroes = Profile.campaign != null ? Profile.campaign.heroes : null;
            if (heroes == null || heroes.Count == 0) return Color.white;
            int main = 0;
            if (!Solo && PartyFlow.State != null)
                foreach (var seat in PartyFlow.State.seats)
                {
                    if (seat.player != player.m_SteamID) continue;
                    main = -1;
                    if (seat.heroes != null) foreach (int id in seat.heroes) if (id >= 0) { main = id; break; }
                    if (main < 0 && seat.recruits.Count > 0) main = seat.recruits[0];
                    break;
                }
            return main >= 0 && main < heroes.Count && heroes[main] != null ? heroes[main].color : Color.white;
        }

        public static void Init() => Router.On(MsgType.PlayerPing, (sender, r) =>
        {
            int level = r.ReadInt32();
            var kind = (Kind)r.ReadByte();
            Vector3 position = r.ReadVector3();
            if (!CanSee || !Seated(sender.m_SteamID) || level != CurrentLevel || kind > Kind.Retreat) return;
            if (!Allowed(sender.m_SteamID)) return;
            Add(sender, kind, position);
        });

        /// <summary>Single player: pings are only shown here (a marker for yourself, and a way to try the wheel).</summary>
        private static bool Solo => !Session || !Session.InLobby;

        private static CSteamID Me => Session ? Session.Me : CSteamID.Nil;

        /// <summary>This player sees pings: a seated defender (or single player), on an island that's playing.</summary>
        private static bool CanSee
        {
            get
            {
                if (CurrentLevel < 0) return false;
                if (Solo) return !SpectatorSession.Active;
                if (Roles.IsMastermind || !PartyFlow.Deployed || !Seated(Session.Me.m_SteamID)) return false;
                return Roles.IsDefender || (SpectatorSession.Active && SpectatorSession.IslandReady());
            }
        }

        /// <summary>This player can ping now (and the middle mouse button is theirs, not the camera's).</summary>
        public static bool CanPing
        {
            get
            {
                if (!CanSee || LoadGate.Blocking) return false;
                var manager = Singleton<IslandGameplayManager>.instance;
                return manager && !manager.states.pause.active;
            }
        }

        private static int CurrentLevel
        {
            get
            {
                Island island = GameAccess.CurrentIsland;
                return island && island.levelNode && island.state == Island.State.Playing ? island.levelNode.index : -1;
            }
        }

        private static bool Seated(ulong player)
        {
            if (PartyFlow.State == null) return false;
            foreach (var seat in PartyFlow.State.seats) if (seat.player == player) return true;
            return false;
        }

        /// <summary>No more than four pings in three seconds from anyone (here as sender, and on every receiver).</summary>
        private static bool Allowed(ulong player)
        {
            if (!recent.TryGetValue(player, out List<float> times)) recent[player] = times = new List<float>();
            times.RemoveAll(t => Time.unscaledTime - t > Window);
            if (times.Count >= PerWindow) return false;
            times.Add(Time.unscaledTime);
            return true;
        }

        /// <summary>Ping a spot for the other defenders (and show it here). False if it's too soon after the last few.</summary>
        public static bool Send(Kind kind, Vector3 position)
        {
            if (!CanPing) return false;
            if (!Allowed(Me.m_SteamID))
            {
                FabricWrapper.PostEvent(FabricID.uiError);
                return false;
            }
            if (!Solo)
            {
                int level = CurrentLevel;
                byte[] packet = Packet.Build(MsgType.PlayerPing, w => { w.Write(level); w.Write((byte)kind); w.Write(position); });
                foreach (var seat in PartyFlow.State.seats)
                {
                    var player = new CSteamID(seat.player);
                    if (player != Session.Me && player != Roles.MastermindId && PartyFlow.IsMember(seat.player))
                        Session.Send(player, packet);
                }
            }
            Add(Me, kind, position);
            return true;
        }

        private static void Add(CSteamID from, Kind kind, Vector3 position)
        {
            // At most three from one player: the oldest goes.
            int theirs = 0;
            for (int i = Active.Count - 1; i >= 0; i--)
                if (Active[i].from == from && ++theirs >= PerPlayer) Active.RemoveAt(i);
            bool mine = from == Me;
            Active.Add(new Ping { from = from, name = mine ? "You" : SteamSession.NameOf(from), kind = kind, position = position, colour = Colour(from), at = Time.unscaledTime });
            ModLog.Info($"Ping from {(mine ? "you" : SteamSession.NameOf(from))}: {Label(kind)}");
            FabricWrapper.PostEvent(mine ? "UI/Menu/Toogle" : "UI/InGame/UnitSelect");
        }

        /// <summary>Every frame: pings fade out, and go with the island they were made on.</summary>
        public static void Update()
        {
            int level = CanSee ? CurrentLevel : -1;
            if (level != shownLevel)
            {
                shownLevel = level;
                Active.Clear();
            }
            Active.RemoveAll(p => Time.unscaledTime - p.at > Duration);
        }
    }
}
