using BadNorthMP.Net;
using HarmonyLib;
using Voxels.TowerDefense;

namespace BadNorthMP.Patches
{
    // Selecting a squad normally slows the battle to 10% (SquadSelector.OnSelectStateChange). With a
    // mastermind connected that would freeze their view too, so the battle stays real-time instead.
    [HarmonyPatch(typeof(TimeManager), nameof(TimeManager.RequestTimeScale))]
    internal static class SelectionSlowMotionPatch
    {
        private static bool Prefix(object requester) =>
            !(requester is SquadSelector && (Roles.IsRemoteDefender || (Roles.IsDefender && Roles.HasMastermind)));
    }
}
