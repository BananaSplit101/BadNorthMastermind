$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sources = (Get-Content "$root/src/Game/WaveSchedule.cs" -Raw) + "`n" + (Get-Content "$root/src/Game/EconomyConfig.cs" -Raw)
$test = @'
namespace BadNorthMP.Game {
 public static class WaveScheduleTests {
  static int checks;
  static void Check(bool ok, string why) { checks++; if (!ok) throw new System.Exception(why); }
  static bool Near(float a, float b) { return System.Math.Abs(a - b) < 0.001f; }
  public static string Run() {
   // Three waves worth 20 each, 20-22 s apart, like an early island.
   var waves = new WaveSchedule(3, 20f, 20f, 22f, 0f, new System.Random(1));
   Check(waves.WaveCount == 3 && waves.WaveIndex == 0 && !waves.AllSailed, "schedule starts at the first wave");
   Check(Near(waves.Budget, 20f), "first wave budget is one wave's value");
   Check(Near(waves.Departure, EconomyConfig.FirstWaveDelay), "first wave waits the planning delay");
   Check(!waves.Due(EconomyConfig.FirstWaveDelay - 0.1f) && waves.Due(EconomyConfig.FirstWaveDelay), "first wave sails on time");
   Check(Near(waves.SecondsLeft(5f), EconomyConfig.FirstWaveDelay - 5f), "countdown");
   // Spending only part of a wave carries half the rest; waiting can't bank a huge wave.
   waves.Sailed(12f, 20f);
   Check(waves.WaveIndex == 1 && Near(waves.Budget, 20f + 8f * EconomyConfig.CarryToNextWave), "half of the unspent budget carries over");
   float gap = waves.Departure - 20f;
   Check(gap >= 20f - 0.001f && gap <= 22f + 0.001f, "next wave uses the island's spacing");
   // Sending early starts the next timer from that moment.
   waves.Sailed(0f, 25f);
   Check(waves.WaveIndex == 2 && waves.FinalWave, "third wave is the final one");
   Check(Near(waves.Budget, 20f + (20f + 8f * EconomyConfig.CarryToNextWave) * EconomyConfig.CarryToNextWave), "an empty wave carries half again, never all of it");
   Check(waves.Departure >= 25f + 20f - 0.001f, "send-now restarts the timer");
   waves.Sailed(0f, 50f);
   Check(waves.AllSailed && Near(waves.Budget, 0f) && !waves.Due(1000f), "after the last wave nothing is left or due");
   waves.Sailed(5f, 60f); Check(waves.WaveIndex == 3, "sailing after the end is ignored");
   // Overspending is impossible (Economy checks), but never produces a negative carry.
   var over = new WaveSchedule(2, 10f, 20f, 22f, 0f, new System.Random(2)); over.Sailed(50f, 1f);
   Check(Near(over.Budget, 10f), "overspent wave carries nothing");
   // Tight native spacing still leaves planning time; missing spacing uses the native 20-22 s.
   var tight = new WaveSchedule(4, 10f, 5f, 6f, 0f, new System.Random(3)); tight.Sailed(0f, 0f);
   Check(tight.Departure >= EconomyConfig.MinWaveSpacing - 0.001f, "minimum spacing enforced");
   var missing = new WaveSchedule(4, 10f, 0f, 0f, 0f, new System.Random(4)); missing.Sailed(0f, 0f);
   Check(missing.Departure >= 20f - 0.001f && missing.Departure <= 22f + 0.001f, "native fallback spacing");
   // Giving up ends the waves; a zero-wave island still has one wave.
   var gaveUp = new WaveSchedule(5, 10f, 20f, 22f, 0f, new System.Random(5)); gaveUp.Cancel();
   Check(gaveUp.AllSailed && !gaveUp.Due(100f), "give up stops all waves");
   Check(new WaveSchedule(0, 10f, 20f, 22f, 0f, new System.Random(6)).WaveCount == 1, "at least one wave");
   return checks + " wave schedule checks passed against the actual WaveSchedule source.";
  }
 }
}
'@
Add-Type -TypeDefinition ($sources + "`n" + $test)
[BadNorthMP.Game.WaveScheduleTests]::Run()
