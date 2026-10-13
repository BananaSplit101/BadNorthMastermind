namespace BadNorthMP.Game
{
    /// <summary>Every tunable number of the mastermind game in one place.</summary>
    internal static class EconomyConfig
    {
        // ---- Waves (WaveSchedule): the island's native wave count and per-wave value, sailing on a timer ----

        /// <summary>Planning time before the first wave sails, once the fight starts.</summary>
        public const float FirstWaveDelay = 20f;

        /// <summary>Waves never sail closer together than this, so the mastermind can plan the next one.</summary>
        public const float MinWaveSpacing = 15f;

        /// <summary>Share of a wave's unspent budget added to the next wave. The last wave's leftover is lost.</summary>
        public const float CarryToNextWave = 0.5f;

        /// <summary>Each defender beyond the first adds this share to the Vikings (the leader's config can change it).</summary>
        public const float DefaultPerExtraDefender = 0.1f;

        /// <summary>How much stronger the Vikings get for a party of this many defenders: wave budgets and the island payout.</summary>
        public static float PartyScale(int defenders, float perExtraDefender) =>
            1f + System.Math.Max(0f, perExtraDefender) * System.Math.Max(0, defenders - 1);

        // ---- War chest: persistent gold for upgrades and commanders ----

        /// <summary>Paid into the war chest during battle.</summary>
        public const int RewardHouse = 5;
        public const int RewardDefender = 1;
        public const int RewardHero = 5;

        /// <summary>Paid into the war chest after every island: base + share of the island's raid value.</summary>
        public const int IslandPayoutBase = 10;
        public const float IslandPayoutPerBounty = 0.25f;

        /// <summary>Extra share of the island's raid value when the defenders are wiped out or flee.</summary>
        public const float WinBonusPerBounty = 0.5f;

        // ---- Viking commanders (CommanderRoster): persistent squads the mastermind owns and controls ----

        public const int MaxCommanders = 4;
        public const int MaxCommandersDeployed = 2;
        /// <summary>Viking commander traits (Game.VikingTrait, 1..this); 0 means none yet.</summary>
        public const int VikingTraitCount = 11;

        /// <summary>Price = this × the type's unit cost × its squad size, from the war chest.</summary>
        public const int CommanderCostMultiplier = 3;

        /// <summary>Each commander already owned makes the next one this much dearer (0.5: +50% each).</summary>
        public const float CommanderPriceStepPerOwned = 0.5f;

        /// <summary>Selling a commander gives back this share of what it cost.</summary>
        public const float CommanderSellBack = 0.25f;

        /// <summary>Vikings in a commander's squad, by VikingAgent.Type (balance numbers, tuned by playtesting):
        /// Swordsmen 9, Shieldbearers 7, Archers 6, Brutes 3, Berserkers 5, Axe throwers 7, Brute archers 3, Champions 7.
        /// Count upgrades add free Vikings on top.</summary>
        public static readonly int[] CommanderSquadSize = { 9, 7, 6, 3, 5, 7, 3, 7 };

        // ---- Ships ----

        public const int MaxShipsPerWave = 4;

        /// <summary>A commander's squad may spread over more ships than a wave (9 big Vikings take room).</summary>
        public const int MaxShipsPerCommander = 6;

        /// <summary>Beaches one wave may split its Vikings over.</summary>
        public const int MaxLandingsPerWave = 4;

        /// <summary>Seconds between ships of one wave launching.</summary>
        public const float ShipStagger = 0.4f;
    }
}
