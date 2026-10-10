using HarmonyLib;
using Voxels.TowerDefense;

namespace BadNorthMP.Patches
{
    // Every player-issued squad order funnels through the static SquadMover helper
    // (Navigator for mouse, TouchMoveConfirmer for touch, JoystickMoveAbility for gamepad).
    // Apply the same ownership checks to native mouse, touch, and gamepad orders.

    [HarmonyPatch(typeof(SquadMover), nameof(SquadMover.MoveTo))]
    internal static class MoveToPatch
    {
        private static bool Prefix(EnglishSquad squad, NavSpot newNavSpot)
        {
            // Remote defenders forward the order to the leader (MoveTo deselects the squad afterwards).
            if (Net.Roles.IsRemoteDefender) { Net.RemoteDefender.Move(squad, newNavSpot, deselect: true); return false; }
            if (!Net.PartyFlow.CanOrder(squad, newNavSpot)) { Net.RemoteDefender.Refuse(squad); return false; }
            return true;
        }
    }

    // Also called by MoveTo, and directly by abilities (pike charge, plunge attack).
    [HarmonyPatch(typeof(SquadMover), nameof(SquadMover.MoveToDirect))]
    internal static class MoveToDirectPatch
    {
        private static bool Prefix(EnglishSquad squad, NavSpot newNavSpot)
        {
            if (Net.Roles.IsRemoteDefender) { Net.RemoteDefender.Move(squad, newNavSpot); return false; }
            if (!Net.PartyFlow.CanOrder(squad, newNavSpot)) { Net.RemoteDefender.Refuse(squad); return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(SquadMover), nameof(SquadMover.SwapWith))]
    internal static class SwapWithPatch
    {
        private static bool Prefix(EnglishSquad squad, NavSpotController other)
        {
            if (Net.Roles.IsRemoteDefender) { Net.RemoteDefender.Move(squad, other ? other.navSpot : null, deselect: true); return false; }
            if (!Net.PartyFlow.CanOrder(squad, other ? other.navSpot : null)) { Net.RemoteDefender.Refuse(squad); return false; }
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
