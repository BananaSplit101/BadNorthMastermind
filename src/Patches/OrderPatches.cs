using HarmonyLib;
using Voxels.TowerDefense;

namespace BadNorthMP.Patches
{
    // Every player-issued squad order (Navigator for mouse, TouchMoveConfirmer for touch, JoystickMoveAbility for
    // gamepad) goes through the static SquadMover helper, so ownership is checked there.

    // MoveTo deselects the squad afterwards.
    [HarmonyPatch(typeof(SquadMover), nameof(SquadMover.MoveTo))]
    internal static class MoveToPatch
    {
        private static bool Prefix(EnglishSquad squad, NavSpot newNavSpot) => OwnedOrder.Allow(squad, newNavSpot, deselect: true);
    }

    // Also called by MoveTo, and directly by abilities (pike charge, plunge attack).
    [HarmonyPatch(typeof(SquadMover), nameof(SquadMover.MoveToDirect))]
    internal static class MoveToDirectPatch
    {
        private static bool Prefix(EnglishSquad squad, NavSpot newNavSpot) => OwnedOrder.Allow(squad, newNavSpot, deselect: false);
    }

    [HarmonyPatch(typeof(SquadMover), nameof(SquadMover.SwapWith))]
    internal static class SwapWithPatch
    {
        private static bool Prefix(EnglishSquad squad, NavSpotController other) =>
            OwnedOrder.Allow(squad, other ? other.navSpot : null, deselect: true);
    }

    internal static class OwnedOrder
    {
        // Remote defenders forward the order to the leader; a squad the player doesn't own is refused.
        public static bool Allow(EnglishSquad squad, NavSpot target, bool deselect)
        {
            if (Net.Roles.IsRemoteDefender) { Net.RemoteDefender.Move(squad, target, deselect); return false; }
            if (!Net.PartyFlow.CanOrder(squad, target)) { Net.RemoteDefender.Refuse(squad); return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(SquadSelector), nameof(SquadSelector.SelectSquad), typeof(EnglishSquad), typeof(bool))]
    internal static class SelectSquadPatch
    {
        private static bool Prefix(EnglishSquad newSelection)
        {
            if (newSelection && !Net.PartyFlow.CanSelect(newSelection)) { Net.RemoteDefender.Refuse(newSelection); return false; }
            return true;
        }
    }
}
