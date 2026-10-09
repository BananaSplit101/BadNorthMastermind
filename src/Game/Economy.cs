using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Game
{
    /// <summary>
    /// Defender side (authoritative): the mastermind's gold on the current island. Income trickles in once
    /// the fight starts, up to the island's cap; rewards for burning houses and killing defenders come on top.
    /// </summary>
    internal static class Economy
    {
        public static bool Active { get; private set; }
        public static float Gold { get; private set; }
        public static float Cap { get; private set; }
        public static float IncomePerSecond { get; private set; }
        public static float IncomeReceived { get; private set; }
        public static bool GaveUp { get; private set; }

        public static float IncomeLeft => Mathf.Max(0f, Cap - IncomeReceived);
        public static bool IncomeDone => IncomeReceived >= Cap;

        private static float capReachedAt = -1f;
        private static float lastSpendAt;
        private static Island island;

        /// <summary>Called when lifetime income is spent, the grace period ran out, or the mastermind gave up.</summary>
        public static bool Done
        {
            get
            {
                if (!Active)
                    return true;
                if (GaveUp)
                    return true;
                if (!IncomeDone)
                    return false;
                if (Gold < CheapestUnit())
                    return true;
                float since = Mathf.Max(capReachedAt, lastSpendAt);
                return Time.time - since > EconomyConfig.GraceAfterCapSeconds;
            }
        }

        public static void Begin(Island battleIsland)
        {
            End();
            island = battleIsland;
            LevelState level = island.levelNode.levelState;
            Cap = level.totalBounty * EconomyConfig.CapMultiplier;
            float spacing = level.minWaveSpacing > 0 && level.maxWaveSpacing > 0
                ? (level.minWaveSpacing + level.maxWaveSpacing) * 0.5f
                : EconomyConfig.DefaultWaveSpacing;
            IncomePerSecond = Cap / Mathf.Max(EconomyConfig.MinIncomeSeconds, level.wavesCount * spacing);
            Gold = MastermindSession.TakeCarriedGold() + level.bountyPerWave;
            IncomeReceived = 0f;
            GaveUp = false;
            capReachedAt = -1f;
            lastSpendAt = Time.time;
            Active = true;
            if (island.village)
                island.village.onHouseDestroyed += OnHouseDestroyed;
            ModLog.Info($"Mastermind economy: cap {Cap:0}, income {IncomePerSecond:0.00}/s, starting gold {Gold:0}");
        }

        public static void End()
        {
            if (!Active)
                return;
            if (island && island.village)
                island.village.onHouseDestroyed -= OnHouseDestroyed;
            MastermindSession.Carry(Gold * EconomyConfig.CarryOver);
            ModLog.Info($"Mastermind economy ended: {Gold:0} gold left, {Gold * EconomyConfig.CarryOver:0} carried over");
            Active = false;
            island = null;
        }

        /// <summary>Income only flows while the fight is on (not during the defender's loadout).</summary>
        public static void Tick(float dt)
        {
            if (!Active || IncomeDone)
                return;
            var manager = Singleton<IslandGameplayManager>.instance;
            if (!manager || !manager.states.running.active)
                return;
            float amount = Mathf.Min(IncomePerSecond * dt, Cap - IncomeReceived);
            IncomeReceived += amount;
            Gold += amount;
            if (IncomeDone && capReachedAt < 0)
                capReachedAt = Time.time;
        }

        public static bool TrySpend(float amount)
        {
            if (!Active || amount > Gold + 0.001f)
                return false;
            Gold -= amount;
            lastSpendAt = Time.time;
            return true;
        }

        public static void GiveUp()
        {
            if (Active)
                GaveUp = true;
        }

        public static void Reward(int amount, string reason)
        {
            if (!Active || amount <= 0)
                return;
            Gold += amount;
            Net.MastermindNet.SendResult(true, $"+{amount} gold: {reason}");
        }

        public static void OnDefenderKilled(bool hero) =>
            Reward(hero ? EconomyConfig.RewardHero : EconomyConfig.RewardDefender, hero ? "hero killed" : "defender killed");

        private static void OnHouseDestroyed(House house) => Reward(EconomyConfig.RewardHouse, "house burned");

        public static float CheapestUnit()
        {
            List<VikingReference> types = AvailableTypes(island);
            return types.Count == 0 ? float.MaxValue : types.Min(t => UnitCost(t));
        }

        public static int UnitCost(VikingReference type) => Mathf.Max(1, type.bounty);

        /// <summary>Viking types the mastermind can send on this island.</summary>
        public static List<VikingReference> AvailableTypes(Island forIsland)
        {
            var types = new List<VikingReference>();
            if (!forIsland || forIsland.levelNode == null)
                return types;
            foreach (VikingReference v in forIsland.levelNode.enemies)
            {
                if (v && v.agent && !types.Exists(t => t.type == v.type))
                    types.Add(v);
            }
            return types;
        }
    }
}
