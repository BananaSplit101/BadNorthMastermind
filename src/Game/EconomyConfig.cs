namespace BadNorthMP.Game
{
    /// <summary>Every tunable number of the mastermind game in one place.</summary>
    internal static class EconomyConfig
    {
        /// <summary>Island gold cap = the island's normal raid budget (LevelState.totalBounty) times this.</summary>
        public const float CapMultiplier = 1f;

        /// <summary>Fallback battle length per wave when the island doesn't specify wave spacing.</summary>
        public const float DefaultWaveSpacing = 21f;

        /// <summary>Income is spread over at least this many seconds.</summary>
        public const float MinIncomeSeconds = 30f;

        /// <summary>Share of unspent gold carried to the next island.</summary>
        public const float CarryOver = 0.5f;

        /// <summary>Once all island income has arrived, the mastermind has this long to keep spending.</summary>
        public const float GraceAfterCapSeconds = 45f;

        public const int RewardHouse = 5;
        public const int RewardDefender = 1;
        public const int RewardHero = 5;

        public const int MaxShipsPerWave = 4;

        /// <summary>Seconds between ships of one wave launching.</summary>
        public const float ShipStagger = 0.4f;
    }
}
