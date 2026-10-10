using System.Collections.Generic;
using Steamworks;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Choosing who goes, once an island has loaded for everyone: the leader shares out the squads
    /// (PartyFlow.PlanDeployment), each defender picks which of their commanders go and presses Ready (UI.DeployPanel),
    /// and when everyone is ready the leader lands them. A player who leaves is skipped; after Timeout the leader lands
    /// everyone's current picks, so one stuck player can't hold the party forever.
    /// </summary>
    internal static class DeployFlow
    {
        public const float Timeout = 120f;

        /// <summary>The deployment being chosen (or the last one), as the leader last said.</summary>
        public static PartyDeployment Current { get; private set; }
        private static float heardAt = -100f, deadline, nextBroadcast;
        private static IslandGameplayManager manager; // leader: the island waiting for its squads
        private static int lastToken;

        private static SteamSession Session => SteamSession.Instance;

        /// <summary>Players are choosing right now (and the leader's word is fresh).</summary>
        public static bool Choosing => Current != null && !Current.done &&
            (Roles.IsDefender ? manager != null : Time.unscaledTime - heardAt < 4f && Session.InLobby);

        /// <summary>Leader: this island is already waiting for everyone's choice.</summary>
        public static bool ChoosingFor(IslandGameplayManager islandManager) => Roles.IsDefender && Choosing && manager == islandManager;

        public static void Init()
        {
            Router.On(MsgType.DeployState, (sender, r) =>
            {
                if (sender != Roles.DefenderId || Roles.IsDefender) return;
                var state = PartyDeployment.Read(r);
                var mine = Current != null && Current.token == state.token ? Current.Find(Session.Me.m_SteamID) : null;
                Current = state;
                heardAt = Time.unscaledTime;
                // Our own click may not have reached the leader yet: keep showing it until the leader agrees.
                var echo = state.Find(Session.Me.m_SteamID);
                if (mine != null && echo != null && pendingUntil > Time.unscaledTime && !state.done)
                { echo.picks = mine.picks; echo.ready = mine.ready; }
            });
            Router.On(MsgType.DeployPick, (sender, r) =>
            {
                if (!Roles.IsDefender || Current == null || Current.done) return;
                int token = r.ReadInt32(), count = r.ReadByte();
                if (count > PartyDeployment.SquadLimit) return;
                var heroes = new List<int>();
                for (int i = 0; i < count; i++) heroes.Add(r.ReadByte());
                bool ready = r.ReadBoolean();
                if (!Current.Pick(sender.m_SteamID, token, heroes, ready))
                    ModLog.Warn($"Deployment pick from {SteamSession.NameOf(sender)} refused");
                Broadcast(); // the truth either way
            });
        }

        /// <summary>Leader (PartyDeployPatch): start choosing for a loaded island. False: nobody can fight.</summary>
        public static bool Begin(IslandGameplayManager islandManager)
        {
            var plan = PartyFlow.PlanDeployment(islandManager.island, ++lastToken);
            if (plan.Squads().Count == 0) return false;
            Current = plan;
            manager = islandManager;
            deadline = Time.unscaledTime + Timeout;
            ModLog.Info($"Island {plan.level}: the defenders choose their commanders");
            Broadcast();
            return true;
        }

        /// <summary>Every frame (MultiplayerGame). The leader lands the squads once everyone is ready.</summary>
        public static void Update()
        {
            if (!Roles.IsDefender || manager == null || Current == null || Current.done) return;
            if (!manager || !manager.island || manager.states.running.active)
            {
                // The island went away (or started some other way): nothing to land.
                manager = null;
                Current.done = true;
                Broadcast();
                return;
            }
            bool changed = false;
            foreach (var seat in Current.seats)
                if (seat.Choosing && !PartyFlow.IsMember(seat.player)) changed |= Current.Drop(seat.player);
            Current.secondsLeft = Mathf.Max(0f, deadline - Time.unscaledTime);
            if (Current.AllReady || Current.secondsLeft <= 0f)
            {
                Current.FillEmpty();
                List<int> squads = Current.Squads();
                if (!Current.AllReady) ModLog.Warn("Deploying without everyone ready: the choice took longer than " + Timeout + " s");
                Current.done = true;
                Broadcast();
                var island = manager;
                manager = null;
                if (squads.Count == 0) { island.states.loadout.SetActive(true); return; }
                ModLog.Info($"Island {Current.level}: deploying {squads.Count} squads");
                PartyFlow.DeploySquads(island, squads);
                return;
            }
            if (changed || Time.unscaledTime >= nextBroadcast) Broadcast();
        }

        private static void Broadcast()
        {
            if (Current == null || !Session.InLobby) return;
            nextBroadcast = Time.unscaledTime + 1f;
            heardAt = Time.unscaledTime;
            Session.Broadcast(Packet.Build(MsgType.DeployState, Current.Write));
        }

        // ---- This player's choice -----------------------------------------------------------------

        private static float pendingUntil;

        public static DeploySeat Mine => Current != null ? Current.Find(Session.Me.m_SteamID) : null;

        /// <summary>Pick or unpick one of our commanders (choosing again un-readies).</summary>
        public static void Toggle(int hero)
        {
            var seat = Mine;
            if (!Choosing || seat == null || !seat.Choosing || !seat.candidates.Contains(hero)) return;
            Send(PartyDeployment.Toggle(seat.picks, hero, seat.quota), false);
        }

        public static void SetReady(bool ready)
        {
            var seat = Mine;
            if (!Choosing || seat == null || !seat.Choosing) return;
            Send(seat.picks, ready);
        }

        private static void Send(List<int> picks, bool ready)
        {
            var seat = Mine;
            if (ready && (picks.Count < 1 || picks.Count > seat.quota)) return;
            seat.picks = new List<int>(picks);
            seat.ready = ready;
            if (Roles.IsDefender)
            {
                if (picks.Count > 0) Current.Pick(Session.Me.m_SteamID, Current.token, picks, ready);
                Broadcast();
                return;
            }
            pendingUntil = Time.unscaledTime + 2f;
            // An empty choice is only shown here; the leader keeps our last real one until we pick again.
            if (picks.Count == 0) return;
            int token = Current.token;
            Session.Send(Roles.DefenderId, Packet.Build(MsgType.DeployPick, w =>
            {
                w.Write(token);
                w.Write((byte)picks.Count);
                foreach (int hero in picks) w.Write((byte)hero);
                w.Write(ready);
            }));
        }
    }
}
