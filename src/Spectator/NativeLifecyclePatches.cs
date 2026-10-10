using System.Collections;
using HarmonyLib;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI;
using Voxels.TowerDefense.Upgrades;

namespace BadNorthMP.Spectator
{
    // Native menus and ambience can receive a final Update after IWipeIsland has cleared their island.
    [HarmonyPatch(typeof(PauseMenu), nameof(PauseMenu.AllowSafeExit))]
    internal static class SpectatorPauseExitAvailabilityPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!SpectatorSession.Active || GameAccess.CurrentIsland) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(PauseMenu), nameof(PauseMenu.AllowAbandon))]
    internal static class SpectatorPauseAbandonAvailabilityPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!SpectatorSession.Active || GameAccess.CurrentIsland) return true;
            __result = false;
            return false;
        }
    }

    // The game's hover check assumes every squad it lists is a live defender squad. On a mirrored battle some aren't
    // (or are already gone), and it threw every frame the pointer moved over the island: nothing is under it then.
    [HarmonyPatch(typeof(SquadSelector), nameof(SquadSelector.GetSquadFromRaycast),
        new[] { typeof(UnityEngine.Vector2), typeof(bool) }, new[] { ArgumentType.Normal, ArgumentType.Out })]
    internal static class SpectatorSquadHoverPatch
    {
        private static System.Exception Finalizer(System.Exception __exception, ref EnglishSquad __result, ref bool wantsHoverEffect)
        {
            if (!(__exception is System.NullReferenceException) || !SpectatorSession.Active) return __exception;
            __result = null;
            wantsHoverEffect = false;
            return null;
        }
    }

    [HarmonyPatch(typeof(IslandAmbiance), "Update")]
    internal static class SpectatorAmbianceLifecyclePatch
    {
        private static bool Prefix(IslandAmbiance __instance)
        {
            if (!SpectatorSession.Active) return true;
            return __instance.island && __instance.island.vikings &&
                Traverse.Create(__instance).Field<IEnumerator>("waterfallUpdate").Value != null;
        }
    }

    [HarmonyPatch(typeof(HouseTargetableAbility), nameof(HouseTargetableAbility.IsAvailable))]
    internal static class SpectatorHealingAvailabilityPatch
    {
        private static bool Prefix(HouseTargetableAbility __instance, ref bool __result)
        {
            if (!SpectatorSession.Active) return true;
            if (__instance && __instance.squad && __instance.squad.livingAgents != null && __instance.locations != null)
                return true;
            // A closing ability button must not query a squad that has already been wiped.
            __result = false;
            return false;
        }
    }
}
