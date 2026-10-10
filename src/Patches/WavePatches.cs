using HarmonyLib;
using Voxels.TowerDefense;

namespace BadNorthMP.Patches
{
    // RaidDriver.Update -> Raid.MaybeLaunchWaves (timer) -> Raid.BeginNextWave -> StartCoroutine(Wave.BeginWave).
    // Against a mastermind the native timer never launches a wave; CustomWaves sails the mastermind's waves instead.

    [HarmonyPatch(typeof(Raid), nameof(Raid.MaybeLaunchWaves))]
    internal static class MaybeLaunchWavesPatch
    {
        private static bool Prefix(Raid __instance, ref bool __result, ref int ___waveIndex)
        {
            if (!BadNorthMP.Net.MultiplayerGame.MastermindControlsWaves)
                return true;

            // Keep the bookkeeping the original does (advance past a fully launched wave),
            // but never start a wave from the timer.
            var waves = __instance.waves;
            if (___waveIndex < waves.Count && waves[___waveIndex].triggered && waves[___waveIndex].haveAllLaunched)
                ___waveIndex++;
            __result = false;
            return false;
        }
    }
}
