using System;
using System.Collections.Generic;
using BadNorthMP.Net;
using BadNorthMP.Spectator;
using Steamworks;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Telemetry
{
    /// <summary>
    /// What the game was doing, for a report: this player's role, where they are (menu, map, loading, battle,
    /// watching), the lobby, the party and loading state, the mastermind's view of the battle, the island. Read on the
    /// main thread; anything that can't be read is left out rather than failing the report.
    /// </summary>
    internal static class GameContext
    {
        public static string Role()
        {
            SteamSession session = SteamSession.Instance;
            if (!session || !session.InLobby) return "solo";
            if (Roles.IsMastermind) return "mastermind";
            if (Roles.IsDefender) return "leader";
            return "defender";
        }

        public static string Phase()
        {
            try
            {
                if (LoadGate.Blocking) return "waiting";
                var stack = Singleton<Voxels.TowerDefense.Stack>.instance;
                if (stack && stack.stateLoading && stack.stateLoading.gameObject.activeInHierarchy) return "loading";
                if (SpectatorSession.Active) return SpectatorSession.IslandReady() ? "watching" : "loading";
                if (GameAccess.CurrentIsland) return "battle";
                if (Profile.campaign != null) return "map";
                return "menu";
            }
            catch (Exception) { return "unknown"; }
        }

        public static ulong LobbyId()
        {
            SteamSession session = SteamSession.Instance;
            return session && session.InLobby ? session.Lobby.m_SteamID : 0UL;
        }

        public static string Level()
        {
            try
            {
                Island island = GameAccess.CurrentIsland;
                return island && island.levelNode != null ? island.levelNode.index.ToString() : "";
            }
            catch (Exception) { return ""; }
        }

        /// <summary>"role|phase|lobby|level", stamped on every log line.</summary>
        public static string Tag() => Role() + "|" + Phase() + "|" + Scrub.LobbyLabel(LobbyId()) + "|" + Level();

        /// <summary>The fuller picture, for an incident or an error.</summary>
        public static Dictionary<string, object> Snapshot()
        {
            var map = new Dictionary<string, object>();
            Try(map, () =>
            {
                SteamSession session = SteamSession.Instance;
                map["in_lobby"] = session && session.InLobby;
                if (session && session.InLobby)
                {
                    map["members"] = session.Members().Count;
                    map["is_host"] = session.Me == session.Owner;
                    map["versions_match"] = Updater.Compatible;
                    var versions = new List<object>();
                    foreach (CSteamID member in session.Members())
                        versions.Add(Scrub.Label(member.m_SteamID) + " " + SteamMatchmaking.GetLobbyMemberData(session.Lobby, member, "bnmp_client_version") +
                            " " + (member == Roles.MastermindId ? "mastermind" : member == Roles.DefenderId ? "leader" : "defender"));
                    map["member_versions"] = versions;
                }
            });
            Try(map, () =>
            {
                map["party_phase"] = PartyFlow.State.phase;
                map["party_seats"] = PartyFlow.State.seats.Count;
            });
            Try(map, () =>
            {
                map["load_blocking"] = LoadGate.Blocking;
                map["map_waiting"] = LoadGate.MapWaiting;
                map["spectating"] = SpectatorSession.Active;
                map["load_step"] = SpectatorSession.LoadStep;
            });
            Try(map, () =>
            {
                if (!MastermindNet.HasState) return;
                MasterState s = MastermindNet.State;
                map["mm_state_age_s"] = Math.Round(Time.unscaledTime - MastermindNet.LastStateTime, 1);
                map["mm_in_battle"] = s.inBattle;
                map["mm_wave"] = s.waveIndex;
                map["mm_waves"] = s.waveCount;
                map["mm_done"] = s.done;
            });
            Try(map, () =>
            {
                Island island = GameAccess.CurrentIsland;
                if (!island) return;
                map["island_english"] = island.english != null && island.english.agents != null ? island.english.agents.Count : -1;
                map["island_vikings"] = island.vikings != null && island.vikings.agents != null ? island.vikings.agents.Count : -1;
            });
            Try(map, () =>
            {
                map["fps"] = Math.Round(Reports.Fps, 1);
                map["slowest_frame_ms"] = Math.Round(Reports.SlowestFrameMs, 0);
                map["uptime_s"] = Math.Round(Time.realtimeSinceStartup, 0);
            });
            return map;
        }

        private static void Try(Dictionary<string, object> map, Action read)
        {
            try { read(); }
            catch (Exception e) { map["context_error_" + map.Count] = e.GetType().Name; }
        }
    }
}
