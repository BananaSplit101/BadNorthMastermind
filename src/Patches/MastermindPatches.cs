using System.Collections.Generic;
using System.Reflection;
using BadNorthMP.Game;
using HarmonyLib;
using Voxels;
using Voxels.TowerDefense;

namespace BadNorthMP.Patches
{
    // Raid implements IIslandPlay explicitly. This runs before it spawns the (hidden, offshore) longships:
    // with a mastermind, the generated raid is swapped for a placeholder and the economy starts.
    [HarmonyPatch]
    internal static class MastermindRaidPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Raid), "Voxels.TowerDefense.IIslandPlay.OnIslandPlay");

        private static void Prefix(Raid __instance, Island island)
        {
            if (!CustomWaves.MastermindMode)
                return;
            CustomWaves.ReplaceRaid(__instance);
            Economy.Begin(island);
        }
    }

    [HarmonyPatch(typeof(IslandWinConditions), nameof(IslandWinConditions.AllEnemiesDefeated))]
    internal static class MastermindWinPatch
    {
        // Normally: the last wave has spawned and no Vikings remain. With a mastermind the placeholder wave
        // never spawns, so decide here: the mastermind is out of gold (or gave up), every ship has unloaded,
        // and no Vikings remain.
        private static void Postfix(ref bool __result)
        {
            if (!Economy.Active)
                return;
            Island island = GameAccess.CurrentIsland;
            __result = Economy.Done && CustomWaves.AllLanded && island && island.vikings.agents.Count == 0;
        }
    }

    [HarmonyPatch(typeof(Island), nameof(Island.LeaveIsland))]
    internal static class MastermindLeaveIslandPatch
    {
        private static void Prefix()
        {
            if (Spectator.SpectatorSession.Active)
                return;
            Economy.End();
            CustomWaves.Reset();
        }
    }
}
