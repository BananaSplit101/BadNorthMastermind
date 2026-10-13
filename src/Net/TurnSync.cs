using BadNorthMP.Spectator;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Next Turn without everyone reloading the map: the leader sends the one random roll (the new time of day) and
    /// every player plays the same turn on their own map. A map that can't (still loading, on another turn) asks for
    /// the whole campaign once the leader has saved the turn.
    /// </summary>
    internal static class TurnSync
    {
        /// <summary>A player's own map is playing the leader's turn (the native Next Turn is otherwise the leader's alone).</summary>
        public static bool Replaying { get; private set; }

        // Leader: the turn's save, which every player has played already, is quiet (until this time, in case it never comes).
        private static float quietSaveUntil = -1f;
        // A player who couldn't play the turn: when to ask for the whole campaign.
        private static float requestAt = -1f;

        private static SteamSession Session => SteamSession.Instance;

        public static void Init()
        {
            Router.On(MsgType.NextTurnPlayed, (sender, r) =>
            {
                if (sender != Roles.DefenderId || Roles.IsDefender) return;
                int token = r.ReadInt32(), seed = r.ReadInt32(), turn = r.ReadInt32();
                float day = r.ReadSingle();
                if (token != PartyFlow.State.token) return;
                Replay(seed, turn, day);
            });
        }

        /// <summary>Leader: carry out the party's Next Turn, and tell everyone how it went.</summary>
        public static void Play(CampaignMapUI map)
        {
            var save = Profile.campaign;
            if (!map || save == null) return;
            int before = save.turnCount;
            quietSaveUntil = Time.unscaledTime + 10f;
            Traverse.Create(map).Method("NextTurn").GetValue();
            if (save.turnCount != before + 1)
            {
                quietSaveUntil = -1f;
                return;
            }
            int token = PartyFlow.State.token, seed = save.seed, turn = save.turnCount;
            float day = save.day.value;
            Session.Broadcast(Packet.Build(MsgType.NextTurnPlayed, w => { w.Write(token); w.Write(seed); w.Write(turn); w.Write(day); }));
            ModLog.Info($"Next Turn played (turn {turn}); every player plays it on their own map");
        }

        /// <summary>Leader, from the campaign save hook: is this save the turn's own (no reload for anyone)?</summary>
        public static bool TakeQuietSave()
        {
            bool quiet = quietSaveUntil >= 0f && Time.unscaledTime <= quietSaveUntil;
            quietSaveUntil = -1f;
            return quiet;
        }

        private static void Replay(int seed, int turn, float day)
        {
            var save = Profile.campaign;
            CampaignMapUI map = Object.FindObjectOfType<CampaignMapUI>();
            if (save == null || save.seed != seed || !SpectatorSession.MapSettled || !map || !map.isActiveAndEnabled ||
                map.blockingInput || save.turnCount != turn - 1)
            {
                ModLog.Info($"Can't play Next Turn on this map (turn {(save != null ? save.turnCount : -1)}, leader on {turn}); fetching the campaign");
                requestAt = Time.unscaledTime + 2f;
                return;
            }
            Replaying = true;
            try { Traverse.Create(map).Method("NextTurn").GetValue(); }
            finally { Replaying = false; }
            // The one random roll, as the leader rolled it.
            save.day = day;
            save.turnStartDay = day;
            ModLog.Info($"Played Next Turn on this map (turn {turn})");
        }

        /// <summary>Every frame: ask for the whole campaign once it's time.</summary>
        public static void Update()
        {
            if (requestAt < 0f || Time.unscaledTime < requestAt) return;
            requestAt = -1f;
            if (!Session || !Session.InLobby || Roles.IsDefender || Roles.DefenderId == CSteamID.Nil) return;
            Session.Send(Roles.DefenderId, Packet.Build(MsgType.CampaignRequest));
        }
    }
}
