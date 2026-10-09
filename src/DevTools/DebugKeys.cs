using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthMP.DevTools
{
    /// <summary>Hotkeys that prove we can drive the game from mod code: issue squad orders and launch waves.</summary>
    internal class DebugKeys : MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
                Run("MoveRandom", MoveSquadToRandomSpot);
            if (Input.GetKeyDown(KeyCode.F9))
                Run("NextWave", LaunchNextWave);
            if (Input.GetKeyDown(KeyCode.F10))
                Run("Dump", DumpIsland);
            if (Input.GetKeyDown(KeyCode.F12))
                Run("TestWave", TestCustomWave);
            if (Input.GetKeyDown(KeyCode.F11))
                Run("WatchLastBattle", () => Spectator.SpectatorSession.Instance.WatchLastBattle());
        }

        private static void Run(string name, System.Action action)
        {
            try
            {
                action();
            }
            catch (System.Exception e)
            {
                ModLog.Error($"{name} failed: {e}");
            }
        }

        /// <summary>F8: order the selected squad (or the first selectable one) to a random free nav spot.</summary>
        private static void MoveSquadToRandomSpot()
        {
            if (!GameAccess.IsPlaying)
            {
                ModLog.Warn("F8: not on an island in play");
                return;
            }

            EnglishSquad squad = EnglishSquad.selected;
            if (!squad)
            {
                foreach (Squad candidate in GameAccess.PlayerSquads)
                {
                    var english = candidate as EnglishSquad;
                    if (english && english.selectable)
                    {
                        squad = english;
                        break;
                    }
                }
            }
            if (!squad)
            {
                ModLog.Warn("F8: no selectable squad");
                return;
            }

            var free = new List<NavSpot>();
            foreach (NavSpot spot in GameAccess.CurrentIsland.navSpotter.navSpots)
            {
                if (spot && !spot.isOccupied)
                    free.Add(spot);
            }
            if (free.Count == 0)
            {
                ModLog.Warn("F8: no free nav spots");
                return;
            }

            NavSpot target = free[Random.Range(0, free.Count)];
            if (!squad.navSpotOccupant.canMove)
                ModLog.Warn($"F8: {GameAccess.Describe(squad)} can't move right now (MoveTo will ignore the order)");
            SquadMover.MoveTo(squad, target);
        }

        /// <summary>F9: launch the next Viking wave now instead of waiting for its timer.</summary>
        private static void LaunchNextWave()
        {
            if (!GameAccess.TryLaunchNextWave(out string message))
                ModLog.Warn("F9: " + message);
        }

        /// <summary>F12: launch a mastermind-style custom wave (4 of the island's first Viking type, first beach).</summary>
        private static void TestCustomWave()
        {
            if (!GameAccess.IsPlaying)
            {
                ModLog.Warn("F12: not on an island in play");
                return;
            }
            Island island = GameAccess.CurrentIsland;
            var types = Game.Economy.AvailableTypes(island);
            if (types.Count == 0)
            {
                ModLog.Warn("F12: no Viking types on this island");
                return;
            }
            var units = new List<KeyValuePair<VikingReference, int>> { new KeyValuePair<VikingReference, int>(types[0], 4) };
            if (Game.CustomWaves.TryLaunch(island, 0, units, out string error, out int count))
                ModLog.Info($"F12: launched {count} {types[0].type} at the {Game.BeachChoices.Get(island)[0].label} beach ({Game.BeachChoices.Get(island).Count} beaches)");
            else
                ModLog.Warn("F12: " + error);
        }

        /// <summary>F10: write the island's squads, vikings and waves to the log.</summary>
        private static void DumpIsland()
        {
            Island island = GameAccess.CurrentIsland;
            if (!island)
            {
                ModLog.Warn("F10: no island loaded");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== Island '{island.name}' state={island.state} navSpots={island.navSpotter.navSpots.Count}");

            sb.AppendLine("Player squads:");
            foreach (Squad squad in GameAccess.PlayerSquads)
            {
                var english = squad as EnglishSquad;
                string spot = english && english.navSpotOccupant ? GameAccess.Describe(english.navSpotOccupant.navSpot) : "-";
                string hero = english && english.heroAgent
                    ? $"hero hp {english.heroAgent.health:0.##}/{english.heroAgent.maxHealth:0.##}"
                    : "no hero";
                sb.AppendLine($"  {GameAccess.Describe(squad)} {hero} at {spot}");
            }

            sb.AppendLine($"Vikings on island: {island.vikings.agents.Count} agents in {island.vikings.livingSquads.Count} squads");

            Raid raid = island.raid;
            if (raid && raid.waves != null)
            {
                sb.AppendLine($"Raid: next wave index {GameAccess.RaidWaveIndex(raid)}, time to next {raid.GetTimeToNextWave():0.0}s");
                for (int i = 0; i < raid.waves.Count; i++)
                    sb.AppendLine($"  wave {i + 1}: {GameAccess.Describe(raid.waves[i])}");
            }

            ModLog.Info(sb.ToString());
        }
    }
}
