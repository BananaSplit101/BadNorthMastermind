using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Game
{
    /// <summary>
    /// Leader side (authoritative): the mastermind's army on the current island. Like the base game's raid, the
    /// island has a fixed number of waves that sail on a timer (WaveSchedule). The mastermind plans each wave from
    /// that wave's budget; when the timer runs out the plan sails, or a random wave if nothing was planned.
    /// Battle results and an end-of-island payout go to the war chest (MastermindSession), which pays for upgrades.
    /// </summary>
    internal static class Economy
    {
        public static bool Active { get; private set; }
        public static bool GaveUp { get; private set; }
        public static WaveSchedule Waves { get; private set; }

        /// <summary>Seconds of fighting on this island; the wave timer runs on it.</summary>
        public static float Clock { get; private set; }

        /// <summary>One landing of the planned wave: a beach tile and the Vikings (paid count per type) landing there.</summary>
        public sealed class PlanLanding
        {
            public int tile;
            public readonly List<KeyValuePair<VikingReference, int>> units = new List<KeyValuePair<VikingReference, int>>();
            public float Cost => units.Sum(p => (float)UnitCost(p.Key) * p.Value);
        }

        /// <summary>The planned wave: its landings (each with its own beach), empty when nothing is planned.</summary>
        public static readonly List<PlanLanding> Plan = new List<PlanLanding>();
        public static int PlanTile => Plan.Count > 0 ? Plan[0].tile : -1;
        public static float PlanCost => Plan.Sum(l => l.Cost);

        public static int WaveIndex => Waves != null ? Waves.WaveIndex : 0;
        public static int WaveCount => Waves != null ? Waves.WaveCount : 0;
        public static float WaveBudget => Waves != null ? Waves.Budget : 0f;
        public static float SecondsToDeparture => Waves != null ? Waves.SecondsLeft(Clock) : 0f;

        /// <summary>No more waves will sail: all have, or the mastermind gave up.</summary>
        public static bool Done => !Active || GaveUp || Waves.AllSailed;

        /// <summary>How the island ended (EndOfLevel), for the payout.</summary>
        public static EndOfLevel.Reason Result { get; set; }

        /// <summary>Viking commanders on this island, by commander id.</summary>
        public static readonly Dictionary<int, Squad> DeployedCommanders = new Dictionary<int, Squad>();

        private static Island island;
        private static float entryWarChest;
        private static byte[] entryUpgrades;
        private static List<VikingCommander> entryCommanders = new List<VikingCommander>();
        private static int entryNextCommander = 1;
        private static readonly System.Random random = new System.Random();

        public static void Begin(Island battleIsland)
        {
            End();
            island = battleIsland;
            LevelState level = island.levelNode.levelState;
            MastermindUpgradeSave.EnsureCampaign();
            entryWarChest = MastermindSession.WarChest;
            entryUpgrades = MastermindSession.CopyUpgrades();
            // Snapshot before the rest countdown, so a retry counts down from the same point again.
            entryCommanders = CommanderRoster.Copy(MastermindSession.Commanders);
            entryNextCommander = MastermindSession.NextCommanderId;
            CommanderRoster.IslandBegins(MastermindSession.Commanders);
            MastermindUpgradeSave.Save();
            DeployedCommanders.Clear();
            Clock = 0f;
            Waves = new WaveSchedule(level.wavesCount, level.bountyPerWave, level.minWaveSpacing, level.maxWaveSpacing, Clock, random);
            ClearPlan();
            GaveUp = false;
            Started = false;
            Result = EndOfLevel.Reason.None;
            Active = true;
            if (island.village)
                island.village.onHouseDestroyed += OnHouseDestroyed;
            ModLog.Info($"Mastermind waves: {Waves.WaveCount} waves of {level.bountyPerWave} gold, first sails in {EconomyConfig.FirstWaveDelay:0}s");
        }

        public static void End()
        {
            if (!Active)
                return;
            if (island && island.village)
                island.village.onHouseDestroyed -= OnHouseDestroyed;
            // Only a finished island pays (not leaving mid-battle to the menu).
            if (Result != EndOfLevel.Reason.None && island)
            {
                float total = island.levelNode.levelState.totalBounty;
                float payout = EconomyConfig.IslandPayoutBase + EconomyConfig.IslandPayoutPerBounty * total;
                if (Result == EndOfLevel.Reason.Wiped || Result == EndOfLevel.Reason.Fled)
                    payout += EconomyConfig.WinBonusPerBounty * total;
                MastermindSession.AddToWarChest(Mathf.Round(payout));
                ModLog.Info($"Mastermind island over ({Result}): +{Mathf.Round(payout):0} war chest, now {MastermindSession.WarChest:0}");
                Net.MastermindNet.SendResult(true, $"+{Mathf.Round(payout):0} war chest: island {(Result == EndOfLevel.Reason.Won ? "lost" : "won")}");
            }
            DeployedCommanders.Clear();
            Active = false;
            island = null;
        }

        public static void Restart()
        {
            if (!Active) return;
            if (island && island.village) island.village.onHouseDestroyed -= OnHouseDestroyed;
            // A retry starts from the island's entry state, without the discarded attempt's rewards or purchases.
            MastermindSession.RestoreUpgrades(entryUpgrades, entryWarChest);
            MastermindSession.RestoreCommanders(entryCommanders, entryNextCommander);
            MastermindUpgradeSave.Save();
            DeployedCommanders.Clear();
            Active = false;
            GaveUp = false;
            island = null;
        }

        /// <summary>The game called the first wave on this island (its running state came on).</summary>
        public static bool Started { get; private set; }

        /// <summary>
        /// The fight is on: from the game's first-wave call until the island's end screen. The game's running state
        /// itself isn't used after that: states above it switch while defenders play (it read "not running" mid-fight,
        /// so the mastermind's timer stopped showing although the waves kept sailing).
        /// </summary>
        public static bool FightOn
        {
            get
            {
                var manager = Singleton<IslandGameplayManager>.instance;
                return Active && Started && manager && !manager.states.EndOfLevel.active;
            }
        }

        /// <summary>A wave is due but waits for a defender commander's boat still at sea (commanders sail in).</summary>
        public static bool HeldForLanding => Active && !Done && Waves.Due(Clock) && SailIn.Arriving;

        /// <summary>Every frame: the battle clock runs only while the fight is on, and sails waves when due.</summary>
        public static void Tick(float dt)
        {
            if (!Active || Done)
                return;
            var manager = Singleton<IslandGameplayManager>.instance;
            if (manager && manager.states.running.active) Started = true;
            if (!FightOn)
                return;
            // The timer keeps counting while the defenders' commanders sail in (so the mastermind sees it run);
            // only a wave that comes due before their boats have landed waits for them.
            Clock += dt;
            if (Waves.Due(Clock) && !SailIn.Arriving)
                Depart(out string ignored);
        }

        public static void ClearPlan() => Plan.Clear();

        /// <summary>Set the current wave's plan (already validated against the island's types, beaches and the budget).</summary>
        public static void SetPlan(List<PlanLanding> landings)
        {
            Plan.Clear();
            Plan.AddRange(landings);
        }

        /// <summary>
        /// The current wave sails when its timer runs out (there's no sending early): each planned landing at its
        /// beach (or, if its ships don't fit there, another one), and only the landings that sailed are paid for. With
        /// no plan, or when nothing could land, a random wave like the native raid's sails instead.
        /// </summary>
        public static bool Depart(out string message)
        {
            message = null;
            if (!Active || Done) { message = "no more waves sail on this island"; return false; }
            int wave = Waves.WaveIndex + 1;
            List<BeachChoice> beaches = BeachChoices.Get(island);
            float spent = 0f;
            int unitCount = 0;
            var places = new List<string>();
            foreach (PlanLanding landing in Plan)
            {
                int planned = beaches.FindIndex(b => b.tileIndex == landing.tile);
                if (!TryAtBeaches(Crew(landing.units), planned, beaches, false, out int count, out string where, out string error))
                {
                    ModLog.Warn($"Mastermind: a planned landing of wave {wave} couldn't land anywhere: {error}");
                    continue;
                }
                spent += landing.Cost;
                unitCount += count;
                places.Add($"{count} at the {where} beach");
            }
            bool launched = places.Count > 0;
            if (!launched && DefaultWave(-1, beaches, out spent, out unitCount, out string randomBeach))
            {
                launched = true;
                places.Add($"{unitCount} at the {randomBeach} beach");
            }
            Waves.Sailed(spent, Clock);
            ClearPlan();
            if (launched)
            {
                // Where they land stays in the log; the banner only says how many are coming.
                ModLog.Info($"Mastermind: wave {wave} of {Waves.WaveCount}: {string.Join(", ", places.ToArray())} ({spent:0} gold)");
                message = $"Wave {wave} of {Waves.WaveCount} sailed: {unitCount} Vikings ({spent:0} gold)";
                Net.MultiplayerGame.Instance.Announce(places.Count == 1
                    ? $"The mastermind has sent {unitCount} Vikings!"
                    : $"The mastermind has sent {unitCount} Vikings to {places.Count} beaches!");
            }
            else
                message = $"Wave {wave} of {Waves.WaveCount} couldn't land anywhere; half its gold carries over";
            if (Waves.AllSailed)
            {
                FabricWrapper.PostEvent("Mus/LastWave");
                Net.MultiplayerGame.Instance.Announce("Final wave!");
            }
            ModLog.Info("Mastermind: " + message);
            Net.MastermindNet.SendResult(launched, message);
            return launched;
        }

        private static List<KeyValuePair<VikingReference, int>> Crew(List<KeyValuePair<VikingReference, int>> paid) =>
            paid.Select(p => new KeyValuePair<VikingReference, int>(p.Key,
                MastermindUpgrades.CrewCount(p.Value, MastermindSession.UpgradeLevel(p.Key.type, UnitUpgrade.Count)))).ToList();

        /// <summary>The preferred beach first; when sailing on the timer, then every other beach.</summary>
        private static bool TryAtBeaches(List<KeyValuePair<VikingReference, int>> crew, int preferred, List<BeachChoice> beaches,
            bool onlyPreferred, out int unitCount, out string where, out string error)
        {
            unitCount = 0; where = null; error = "pick a beach to land at";
            var order = new List<int>();
            if (preferred >= 0) order.Add(preferred);
            if (!onlyPreferred)
                foreach (int i in Enumerable.Range(0, beaches.Count).OrderBy(i => random.Next()))
                    if (i != preferred) order.Add(i);
            foreach (int beach in order)
                if (CustomWaves.TryLaunch(island, beach, crew, out error, out unitCount))
                {
                    where = beaches[beach].label;
                    return true;
                }
            return false;
        }

        /// <summary>No plan: one random affordable type with the whole budget, smaller if the ships can't fit it.</summary>
        private static bool DefaultWave(int preferred, List<BeachChoice> beaches, out float spent, out int unitCount, out string where)
        {
            spent = 0f; unitCount = 0; where = null;
            List<VikingReference> types = AvailableTypes(island).Where(t => UnitCost(t) <= Waves.Budget).ToList();
            if (types.Count == 0) return false;
            VikingReference type = types[random.Next(types.Count)];
            for (int paid = (int)(Waves.Budget / UnitCost(type)); paid >= 1; paid = paid * 2 / 3)
            {
                var plan = new List<KeyValuePair<VikingReference, int>> { new KeyValuePair<VikingReference, int>(type, paid) };
                if (TryAtBeaches(Crew(plan), preferred, beaches, false, out unitCount, out where, out string ignored))
                {
                    spent = paid * UnitCost(type);
                    return true;
                }
                if (paid == 1) break;
            }
            return false;
        }

        public static void GiveUp()
        {
            if (!Active) return;
            GaveUp = true;
            Waves.Cancel();
            ClearPlan();
        }

        /// <summary>Battle results pay into the war chest.</summary>
        public static void Reward(int amount, string reason)
        {
            if (!Active || amount <= 0)
                return;
            MastermindSession.AddToWarChest(amount);
            Net.MastermindNet.SendResult(true, $"+{amount} war chest: {reason}");
        }

        public static void OnDefenderKilled(bool hero) =>
            Reward(hero ? EconomyConfig.RewardHero : EconomyConfig.RewardDefender, hero ? "hero killed" : "defender killed");

        private static void OnHouseDestroyed(House house) => Reward(EconomyConfig.RewardHouse, "house burned");

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
