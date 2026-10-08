using HarmonyLib;
using Voxels.TowerDefense;

namespace BadNorthMP.Patches
{
    // RaidDriver.Update -> Raid.MaybeLaunchWaves (timer) -> Raid.BeginNextWave -> StartCoroutine(Wave.BeginWave).
    // For the mastermind mode, MaybeLaunchWaves gets blocked and BeginNextWave is called on the mastermind's command.

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
