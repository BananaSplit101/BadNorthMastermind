using System;
using System.Collections.Generic;
using HarmonyLib;
using Voxels.TowerDefense;
using TargetEntry = RTM.Utilities.Tuple<Voxels.TowerDefense.Upgrades.NavSpotTargetableAbility, Voxels.TowerDefense.NavSpotTargetCache>;

namespace BadNorthMP.Patches
{
    /// <summary>
    /// The targeting manager lends each targeted ability (Pike Charge, Bomb, Mine...) a cache sized for the island and
    /// takes them back when the island is wiped. A watcher's islands are torn down without that wipe, so caches stayed
    /// lent to gone squads, sized for an older island: on a bigger one the targeting loop ran off the end and no
    /// targeted ability found targets again. Before each island: take back the orphans, resize the rest.
    /// </summary>
    [HarmonyPatch(typeof(NavSpotTargetManager), "Voxels.TowerDefense.IslandGameplayManager.ISetupIsland.OnSetup")]
    internal static class TargetCacheRecyclePatch
    {
        private static void Prefix(NavSpotTargetManager __instance, Island island)
        {
            if (!island || !island.navSpotter) return;
            var fields = Traverse.Create(__instance);
            Stack<NavSpotTargetCache> available = fields.Field<Stack<NavSpotTargetCache>>("available").Value;
            List<TargetEntry> inUse = fields.Field<List<TargetEntry>>("inUse").Value;
            if (available == null || inUse == null) return;
            int size = island.navSpotter.navSpots.Count, recycled = 0;
            for (int i = inUse.Count - 1; i >= 0; i--)
            {
                TargetEntry entry = inUse[i];
                if (entry.item2 == null) { inUse.RemoveAt(i); continue; }
                if (!entry.item1)
                {
                    entry.item2.Reset();
                    available.Push(entry.item2);
                    inUse.RemoveAt(i);
                    recycled++;
                }
                else entry.item2.Resize(size);
            }
            if (recycled > 0) ModLog.Info($"Targeting: took back {recycled} caches left over from earlier islands");
        }
    }

    /// <summary>Island teardown on a watcher's screen: an ability button losing focus after its ability is gone.</summary>
    [HarmonyPatch(typeof(Voxels.TowerDefense.UI.ActiveAbilityButton), "OnLostFocus")]
    internal static class AbilityButtonTeardownPatch
    {
        private static Exception Finalizer(Exception __exception) => __exception is NullReferenceException ? null : __exception;
    }

    /// <summary>Island teardown: a dying soldier's selection glow after its renderer is gone.</summary>
    [HarmonyPatch(typeof(AgentSelected), "UpdateSelection")]
    internal static class AgentSelectedTeardownPatch
    {
        private static Exception Finalizer(Exception __exception) => __exception is NullReferenceException ? null : __exception;
    }
}
