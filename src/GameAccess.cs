using System.Collections.Generic;
using HarmonyLib;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthMP
{
    /// <summary>Helpers for reaching into the game's island/squad/raid state.</summary>
    internal static class GameAccess
    {
        /// <summary>The island currently loaded for gameplay, or null on menus and the campaign map.</summary>
        public static Island CurrentIsland
        {
            get
            {
                var manager = Singleton<IslandGameplayManager>.instance;
                return manager ? manager.island : null;
            }
        }

        public static bool IsPlaying
        {
            get
            {
                var island = CurrentIsland;
                return island && island.state == Island.State.Playing;
            }
        }

        public static List<Squad> PlayerSquads
        {
            get
            {
                var island = CurrentIsland;
                return island && island.english ? island.english.livingSquads : new List<Squad>();
            }
        }

        public static int SquadIndex(Squad squad) => squad ? PlayerSquads.IndexOf(squad) : -1;

        /// <summary>Raid.waveIndex is private; it is the next wave to launch (or the one launching).</summary>
        public static int RaidWaveIndex(Raid raid) => Traverse.Create(raid).Field<int>("waveIndex").Value;

        public static string Describe(Squad squad)
        {
            if (!squad)
                return "<none>";
            var english = squad as EnglishSquad;
            string hero = english && english.hero != null ? english.hero.dbgName : squad.name;
            return $"squad#{SquadIndex(squad)} '{hero}' ({squad.livingAgents.Count} alive)";
        }

        public static string Describe(NavSpot spot)
        {
            if (!spot)
                return "<none>";
            return $"navspot#{spot.index} @ {spot.navPos.wPos}";
        }

        public static string Describe(Wave wave)
        {
            if (!wave)
                return "<none>";
            int landings = 0, agents = 0;
            foreach (ShipGroup group in wave.shipGroups)
            {
                landings += group.landings.Count;
                foreach (Landing landing in group.landings)
                    agents += landing.agentCount;
            }
            return $"start={wave.waveStartTime:0.0}s groups={wave.shipGroups.Count} ships={landings} " +
                   $"vikings={agents} bounty={wave.bounty} triggered={wave.triggered} launched={wave.haveAllLaunched}";
        }
    }
}
