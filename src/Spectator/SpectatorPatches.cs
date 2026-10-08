using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Voxels;
using Voxels.TowerDefense;

namespace BadNorthMP.Spectator
{
    // While spectating, the island loads but none of the gameplay runs locally: no loadout, no squads,
    // no Viking raid, no win/lose checks. The defender's units are shown by the puppet layer instead.

    [HarmonyPatch(typeof(IslandGameplayManager), "EnterLevel")]
    internal static class SpectatorEnterLevelPatch
    {
        // Normally opens the loadout screen (or spawns squads for tutorials). The island root state is
        // already active from EnterIsland, which is all a spectator needs.
        private static bool Prefix() => !SpectatorSession.Active;
    }

    [HarmonyPatch(typeof(SquadSpawner), nameof(SquadSpawner.SpawnEnglishSquads))]
    internal static class SpectatorSquadSpawnPatch
    {
        private static bool Prefix() => !SpectatorSession.Active;
    }

    [HarmonyPatch(typeof(IslandWinConditions), "Update")]
    internal static class SpectatorWinConditionsPatch
    {
        // With no English agents the island would immediately count as lost.
        private static bool Prefix() => !SpectatorSession.Active;
    }

    [HarmonyPatch(typeof(RaidDriver), "Update")]
    internal static class SpectatorRaidDriverPatch
    {
        private static bool Prefix() => !SpectatorSession.Active;
    }

    [HarmonyPatch(typeof(LevelPreparePhase), "Update")]
    internal static class SpectatorPreparePhasePatch
    {
        private static bool Prefix() => !SpectatorSession.Active;
    }

    // Raid implements IIslandPlay explicitly; it spawns every longship (hidden, offshore) up front.
    [HarmonyPatch]
    internal static class SpectatorRaidPlayPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Raid), "Voxels.TowerDefense.IIslandPlay.OnIslandPlay");

        private static bool Prefix(ref IEnumerator<GenInfo> __result)
        {
            if (!SpectatorSession.Active)
                return true;
            __result = Nothing();
            return false;
        }

        private static IEnumerator<GenInfo> Nothing()
        {
            yield break;
        }
    }
}
