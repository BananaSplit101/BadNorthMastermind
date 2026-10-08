using HarmonyLib;
using Voxels.TowerDefense;

namespace BadNorthMP.Patches
{
    // Every player-issued squad order funnels through the static SquadMover helper
    // (Navigator for mouse, TouchMoveConfirmer for touch, JoystickMoveAbility for gamepad).
    // These hooks are where defender orders will later be captured and sent to the host.

    [HarmonyPatch(typeof(SquadMover), nameof(SquadMover.MoveTo))]
    internal static class MoveToPatch
    {
        private static void Prefix(EnglishSquad squad, NavSpot newNavSpot)
        {
            ModLog.Info($"MoveTo {GameAccess.Describe(squad)} -> {GameAccess.Describe(newNavSpot)} canMove={squad.navSpotOccupant.canMove}");
        }
    }

    // Also called by MoveTo, and directly by abilities (pike charge, plunge attack).
    [HarmonyPatch(typeof(SquadMover), nameof(SquadMover.MoveToDirect))]
    internal static class MoveToDirectPatch
    {
        private static void Prefix(EnglishSquad squad, NavSpot newNavSpot, bool showPath)
        {
            ModLog.Info($"  MoveToDirect {GameAccess.Describe(squad)} -> {GameAccess.Describe(newNavSpot)} showPath={showPath}");
        }
    }

    [HarmonyPatch(typeof(SquadMover), nameof(SquadMover.SwapWith))]
    internal static class SwapWithPatch
    {
        private static void Prefix(EnglishSquad squad, NavSpotController other)
        {
            ModLog.Info($"SwapWith {GameAccess.Describe(squad)} <-> {GameAccess.Describe(other ? other.navSpot : null)}");
        }
    }

    [HarmonyPatch(typeof(SquadSelector), nameof(SquadSelector.SelectSquad), typeof(EnglishSquad), typeof(bool))]
    internal static class SelectSquadPatch
    {
        private static void Prefix(SquadSelector __instance, EnglishSquad newSelection)
        {
            if (newSelection == __instance.selectedSquad)
                return;
            ModLog.Info(newSelection ? $"Select {GameAccess.Describe(newSelection)}" : "Deselect");
        }
    }
}
