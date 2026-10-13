namespace BadNorthMP.Game
{
    /// <summary>
    /// What the mastermind keeps across one multiplayer campaign's islands: the war chest, upgrade levels and commanders.
    /// Lives on the (authoritative) leader, saved per campaign by MastermindUpgradeSave. Wave budgets never carry over.
    /// </summary>
    internal static class MastermindSession
    {
        private static float warChest;
        private static readonly byte[] upgrades = new byte[MastermindUpgrades.TypeCount * 3];
        public static long UpgradeSession { get; private set; } = System.DateTime.UtcNow.Ticks;
        public static int UpgradeRevision { get; private set; }

        public static int UpgradeLevel(Voxels.TowerDefense.VikingAgent.Type type, UnitUpgrade kind)
        {
            return MastermindUpgrades.ValidType(type) && MastermindUpgrades.ValidKind(kind)
                ? upgrades[(int)type * 3 + (int)kind] : 0;
        }

        public static byte[] CopyUpgrades() => (byte[])upgrades.Clone();

        // Only called after the defender validates and pays for a purchase.
        internal static bool AddUpgrade(Voxels.TowerDefense.VikingAgent.Type type, UnitUpgrade kind)
        {
            upgrades[(int)type * 3 + (int)kind]++;
            UpgradeRevision++;
            if (MastermindUpgradeSave.Save()) return true;
            upgrades[(int)type * 3 + (int)kind]--;
            UpgradeRevision--;
            return false;
        }

        /// <summary>The mastermind's Viking commanders, and the id the next one gets.</summary>
        public static readonly System.Collections.Generic.List<VikingCommander> Commanders = new System.Collections.Generic.List<VikingCommander>();
        public static int NextCommanderId { get; private set; } = 1;

        public static VikingCommander Commander(int id) => Commanders.Find(c => c.id == id);

        /// <summary>Only called after the leader validates and charges the purchase.</summary>
        internal static bool AddCommander(int type, int price = 0)
        {
            var commander = new VikingCommander { id = NextCommanderId, type = type, trait = (int)VikingTraits.Random(), price = price };
            Commanders.Add(commander);
            NextCommanderId++;
            if (MastermindUpgradeSave.Save()) return true;
            Commanders.Remove(commander);
            NextCommanderId--;
            return false;
        }

        /// <summary>Only called after the leader validates a sale: the commander leaves and the refund goes to the war chest.</summary>
        internal static bool SellCommander(int id, int refund)
        {
            VikingCommander commander = Commander(id);
            if (commander == null) return false;
            int index = Commanders.IndexOf(commander);
            Commanders.RemoveAt(index);
            warChest += refund;
            if (MastermindUpgradeSave.Save()) return true;
            Commanders.Insert(index, commander);
            warChest -= refund;
            return false;
        }

        internal static void RestoreCommanders(System.Collections.Generic.List<VikingCommander> roster, int nextId)
        {
            Commanders.Clear();
            Commanders.AddRange(CommanderRoster.Copy(roster));
            NextCommanderId = System.Math.Max(1, nextId);
        }

        public static float WarChest => warChest;

        /// <summary>Battle rewards and island payouts.</summary>
        public static void AddToWarChest(float gold)
        {
            if (gold <= 0f || float.IsNaN(gold) || float.IsInfinity(gold)) return;
            MastermindUpgradeSave.EnsureCampaign();
            warChest += gold;
            MastermindUpgradeSave.Save();
        }

        internal static bool TrySpendWarChest(int gold)
        {
            if (gold <= 0 || float.IsNaN(warChest) || float.IsInfinity(warChest) || gold > warChest + 0.001f) return false;
            warChest = System.Math.Max(0f, warChest - gold);
            return true;
        }

        internal static void Refund(int gold) => warChest += gold;

        /// <summary>Set the whole state (loading a save, or undoing a retried island).</summary>
        internal static void RestoreUpgrades(byte[] levels, float bank)
        {
            System.Array.Copy(levels, upgrades, upgrades.Length);
            UpgradeRevision = 0;
            foreach (byte level in upgrades) UpgradeRevision += level;
            warChest = bank;
            UpgradeSession = System.Math.Max(UpgradeSession + 1, System.DateTime.UtcNow.Ticks);
        }

        /// <summary>Testing and debug only: overwrite the war chest.</summary>
        internal static void SetWarChest(float gold)
        {
            warChest = gold;
            MastermindUpgradeSave.Save();
        }

        public static void Reset()
        {
            warChest = 0f;
            Commanders.Clear();
            NextCommanderId = 1;
            System.Array.Clear(upgrades, 0, upgrades.Length);
            UpgradeRevision = 0;
            UpgradeSession = System.Math.Max(UpgradeSession + 1, System.DateTime.UtcNow.Ticks);
            MastermindUpgradeSave.Reset();
            Net.MastermindNet.Reset();
        }
    }
}
