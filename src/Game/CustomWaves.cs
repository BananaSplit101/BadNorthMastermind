using System.Collections.Generic;
using System.Linq;
using BadNorthMP.Net;
using BadNorthMP.Spectator;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthMP.Game
{
    /// <summary>
    /// Defender side: Viking waves composed by the mastermind, built at runtime the same way
    /// Raid.OnIslandFirstEnter/OnIslandPlay build the game's own (Wave > ShipGroup > Landing > ShipLoad).
    /// </summary>
    internal static class CustomWaves
    {
        /// <summary>The defender plays against a mastermind: the game's own raid is replaced.</summary>
        public static bool MastermindMode => Roles.IsDefender && Roles.HasMastermind && !SpectatorSession.Active;

        private static readonly List<Wave> sent = new List<Wave>();
        private static Wave placeholder;

        /// <summary>Every ship sent so far has dropped off its whole crew.</summary>
        public static bool AllLanded => sent.TrueForAll(w => !w || w.haveAllSpawned);

        /// <summary>
        /// Swap the generated raid for a placeholder wave that never launches: the raid logic needs at least one wave
        /// (Raid.AllWavesSpawned reads waves.Last()), and as it never spawns, the island is only won by our own end condition.
        /// </summary>
        public static void ReplaceRaid(Raid raid)
        {
            foreach (Wave wave in raid.waves)
            {
                if (wave)
                    Object.Destroy(wave.gameObject);
            }
            raid.waves.Clear();
            sent.Clear();

            placeholder = raid.landingContainer.gameObject.AddEmptyChild("BNMP Placeholder Wave").AddComponent<Wave>();
            placeholder.raid = raid;
            placeholder.waveStartTime = float.MaxValue;
            raid.waves.Add(placeholder);
            Traverse.Create(raid).Field("waveIndex").SetValue(0);
            ModLog.Info("Mastermind mode: the island's own raid was replaced");
        }

        public static void Reset()
        {
            sent.Clear();
            placeholder = null;
        }

        public static bool CanLaunch(out string error)
        {
            var manager = Singleton<IslandGameplayManager>.instance;
            if (!MastermindMode || !Economy.Active)
            { error = "multiplayer army controls aren't ready"; return false; }
            if (!manager || !manager.island || !Economy.FightOn)
            { error = "waiting for the defender to deploy squads and start the battle"; return false; }
            if (Time.timeScale <= 0f)
            { error = "the defender paused the battle"; return false; }
            error = null;
            return true;
        }

        /// <summary>Build and launch a wave at the given beach. Units are split over ships by deck space.</summary>
        public static bool TryLaunch(Island island, int beachIndex, List<KeyValuePair<VikingReference, int>> units, out string error, out int unitCount) =>
            TryLaunch(island, beachIndex, units, EconomyConfig.MaxShipsPerWave, out error, out unitCount, out _);

        /// <summary>
        /// As above, with its own ship limit (commanders may use more ships than a wave); <paramref name="launched"/> is
        /// the new wave, whose one ship group is one Viking squad.
        /// </summary>
        public static bool TryLaunch(Island island, int beachIndex, List<KeyValuePair<VikingReference, int>> units, int maxShips, out string error, out int unitCount, out Wave launched)
        {
            launched = null;
            unitCount = units.Sum(u => u.Value);
            if (!CanLaunch(out error)) return false;
            Raid raid = island.raid;
            List<BeachChoice> beaches = BeachChoices.Get(island);
            if (!raid || raid.waves == null)
            {
                error = "the island has no raid";
                return false;
            }
            if (!raid.isActiveAndEnabled || !raid.landingContainer || !raid.landingContainer.gameObject.activeInHierarchy)
            {
                error = "the battle's ship container is inactive";
                return false;
            }
            if (beachIndex < 0 || beachIndex >= beaches.Count)
            {
                error = "unknown beach";
                return false;
            }
            if (unitCount <= 0)
            {
                error = "no units chosen";
                return false;
            }

            List<Longship> shipTypes = island.levelNode.possibleShips.Where(s => s).OrderBy(s => s.area).ToList();
            if (shipTypes.Count == 0)
            {
                error = "the island has no ship types";
                return false;
            }
            List<List<KeyValuePair<VikingReference, int>>> shipLoads = PackShips(units, shipTypes.Last().area);
            if (shipLoads.Count > maxShips)
            {
                error = $"that needs {shipLoads.Count} ships; the most at once is {maxShips}";
                return false;
            }

            var wave = raid.landingContainer.gameObject.AddEmptyChild("BNMP Wave").AddComponent<Wave>();
            wave.raid = raid;
            // Raid generation normally supplies these; AddComponent leaves them null.
            wave.approachAudioId = units[0].Key.approachAudioId ?? FabricEventReference.none;
            wave.arriveAudioId = units[0].Key.arriveAudioId ?? FabricEventReference.none;
            wave.timeSpreadGroup = 0f;   // Wave.Awake randomises these
            wave.timeSpreadShip = 1f;
            wave.waveStartTime = 0f;
            ShipGroup group = wave.AddShipGroup(wave.gameObject.AddEmptyChild("Group").AddComponent<ShipGroup>());

            float speed = island.levelNode.diffiucltySettings ? island.levelNode.diffiucltySettings.shipSpeedMultiplier : 1f;
            BeachChoice beach = beaches[beachIndex];
            var placed = new List<Landing>();
            for (int i = 0; i < shipLoads.Count; i++)
            {
                Landing landing = group.AddLanding(group.gameObject.AddEmptyChild("Landing").AddComponent<Landing>());
                landing.Init(island);
                landing.timeOffset = i * EconomyConfig.ShipStagger; // Init randomises this
                foreach (KeyValuePair<VikingReference, int> load in shipLoads[i])
                {
                    ShipLoad shipLoad = landing.gameObject.AddEmptyChild("Load").AddComponent<ShipLoad>();
                    landing.AddShipLoad(shipLoad);
                    shipLoad.vikingRef = load.Key;
                    shipLoad.count = load.Value;
                }
                // Ship size first: TryPlace reads the prefab's radius and length.
                float area = landing.agentArea;
                landing.shipPrefab = shipTypes.FirstOrDefault(s => s.area >= area) ?? shipTypes.Last();

                if (!Place(landing, beach, speed, placed))
                {
                    Object.Destroy(wave.gameObject);
                    error = "no room to land there right now";
                    return false;
                }
            }

            wave.RefreshLandings();
            try
            {
                foreach (Landing landing in group.landings)
                {
                    Longship ship = landing.Spawn();
                    CustomShipOrders.Assign(ship);
                    var animator = ship.GetComponent<Animator>();
                    if (animator)
                    {
                        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        animator.enabled = true;
                    }
                    ship.gameObject.AddComponent<CustomShipWatch>();
                    // Natively every ship becomes a boat to flee on once it has landed and emptied.
                    Evacuation.AddShip(raid, ship);
                }
            }
            catch (System.Exception e)
            {
                Object.Destroy(wave.gameObject);
                ModLog.Error("Custom wave setup failed: " + e);
                error = "wave setup failed; see the campaign leader's log";
                return false;
            }

            // Before the placeholder, so the placeholder stays last.
            int insertAt = placeholder && raid.waves.Contains(placeholder) ? raid.waves.IndexOf(placeholder) : raid.waves.Count;
            raid.waves.Insert(insertAt, wave);
            // The first launch step runs here, so its exceptions can't hide in an unobserved coroutine behind a success reply.
            var launch = wave.BeginWave();
            try
            {
                bool more = launch.MoveNext();
                if (!group.landings.Any(l => l.spawnedShip && l.spawnedShip.isActiveAndEnabled))
                    throw new System.InvalidOperationException("No ship activated during wave launch");
                if (more) raid.StartCoroutine(launch);
            }
            catch (System.Exception e)
            {
                raid.waves.Remove(wave);
                Object.Destroy(wave.gameObject);
                ModLog.Error("Custom wave launch failed: " + e);
                error = "wave could not launch; see the campaign leader's log";
                return false;
            }
            sent.Add(wave);
            launched = wave;
            ModLog.Info($"Launched custom wave: {unitCount} Vikings in {shipLoads.Count} ships at the {beach.label} beach");
            return true;
        }

        private static bool Place(Landing landing, BeachChoice beach, float speed, List<Landing> placed)
        {
            foreach (Beaches.Beach.Pos p in beach.positions)
            {
                if (landing.TryPlace(p.navPos, p.dir, speed, placed))
                    return true;
            }
            foreach (Beaches.Beach.Pos p in beach.positions)
            {
                if (landing.TryPlace(p.navPos, -p.navPos.GetCliffVector(), speed, placed))
                    return true;
            }
            return false;
        }

        /// <summary>Fill ships one after another up to the largest ship's deck area.</summary>
        private static List<List<KeyValuePair<VikingReference, int>>> PackShips(List<KeyValuePair<VikingReference, int>> units, float maxArea)
        {
            var ships = new List<List<KeyValuePair<VikingReference, int>>>();
            var counts = new Dictionary<VikingReference, int>();
            var order = new List<VikingReference>();
            float used = 0f;

            void Flush()
            {
                if (order.Count == 0)
                    return;
                ships.Add(order.Select(v => new KeyValuePair<VikingReference, int>(v, counts[v])).ToList());
                counts.Clear();
                order.Clear();
                used = 0f;
            }

            foreach (KeyValuePair<VikingReference, int> unit in units)
            {
                float area = unit.Key.agent.area;
                for (int i = 0; i < unit.Value; i++)
                {
                    if (used + area > maxArea && order.Count > 0)
                        Flush();
                    if (!counts.ContainsKey(unit.Key))
                    {
                        counts[unit.Key] = 0;
                        order.Add(unit.Key);
                    }
                    counts[unit.Key]++;
                    used += area;
                }
            }
            Flush();
            return ships;
        }
    }
}
