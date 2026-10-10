using System;

namespace BadNorthMP.Game
{
    /// <summary>
    /// The mastermind's waves on one island, paced like the base game's raid: a fixed number of waves that sail on
    /// a timer, each with its own budget. Unspent budget only partly carries to the next wave, so waiting never
    /// builds up one huge wave. Times are battle-clock seconds (Economy.Clock), which run only while the fight is on.
    /// </summary>
    internal sealed class WaveSchedule
    {
        private readonly float bountyPerWave, minSpacing, maxSpacing;
        private readonly Random random;

        public int WaveCount { get; }
        /// <summary>The wave being planned (0-based); WaveCount once every wave has sailed.</summary>
        public int WaveIndex { get; private set; }
        /// <summary>Gold the current wave may spend.</summary>
        public float Budget { get; private set; }
        /// <summary>Battle-clock time the current wave sails.</summary>
        public float Departure { get; private set; }
        public bool AllSailed => WaveIndex >= WaveCount;
        public bool FinalWave => WaveIndex == WaveCount - 1;

        public WaveSchedule(int waves, float bountyPerWave, float minSpacing, float maxSpacing, float now, Random random)
        {
            WaveCount = Math.Max(1, waves);
            this.bountyPerWave = Math.Max(1f, bountyPerWave);
            // Islands without spacing data use the native fallback of 20-22 seconds.
            this.minSpacing = minSpacing > 0f ? minSpacing : 20f;
            this.maxSpacing = maxSpacing > 0f ? Math.Max(this.minSpacing, maxSpacing) : 22f;
            this.random = random;
            Budget = this.bountyPerWave;
            Departure = now + EconomyConfig.FirstWaveDelay;
        }

        public bool Due(float now) => !AllSailed && now >= Departure;

        public float SecondsLeft(float now) => AllSailed ? 0f : Math.Max(0f, Departure - now);

        /// <summary>The current wave sailed having spent <paramref name="spent"/>; open the next one.</summary>
        public void Sailed(float spent, float now)
        {
            if (AllSailed) return;
            float unspent = Math.Max(0f, Budget - Math.Max(0f, spent));
            WaveIndex++;
            if (AllSailed) { Budget = 0f; return; }
            Budget = bountyPerWave + unspent * EconomyConfig.CarryToNextWave;
            Departure = now + Spacing();
        }

        /// <summary>The mastermind gave up: no more waves.</summary>
        public void Cancel()
        {
            WaveIndex = WaveCount;
            Budget = 0f;
        }

        private float Spacing() =>
            Math.Max(EconomyConfig.MinWaveSpacing, minSpacing + (float)random.NextDouble() * (maxSpacing - minSpacing));
    }
}
