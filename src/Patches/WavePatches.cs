using HarmonyLib;
using Voxels.TowerDefense;

namespace BadNorthMP.Patches
{
    // RaidDriver.Update -> Raid.MaybeLaunchWaves (timer) -> Raid.BeginNextWave -> StartCoroutine(Wave.BeginWave).
    // For the mastermind mode, MaybeLaunchWaves gets blocked and BeginNextWave is called on the mastermind's command.

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

    [HarmonyPatch(typeof(Raid), nameof(Raid.BeginNextWave))]
    internal static class BeginNextWavePatch
    {
        private static void Prefix(Raid __instance, int ___waveIndex)
        {
            var waves = __instance.waves;
            if (___waveIndex < 0 || ___waveIndex >= waves.Count)
                return;
            ModLog.Info($"BeginNextWave {___waveIndex + 1}/{waves.Count}: {GameAccess.Describe(waves[___waveIndex])}");
        }
    }
}
