using System;
using System.Collections.Generic;
using System.IO;
using BadNorthMP.Net;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;
using Voxels.TowerDefense.Upgrades;

namespace BadNorthMP.Spectator
{
    /// <summary>
    /// Mastermind side: the spectator island runs the real battle simulation (AI, combat, projectiles, sounds,
    /// animation), fed the defender's units, squad positions and wave launches, and kept faithful by the
    /// defender's authority: local damage is visual only, deaths/health/house fires come from the defender,
    /// and positions are pulled toward the defender's snapshots.
    /// Viking ships are generated identically on both sides (seeded raid) and matched by their place in the raid.
    /// </summary>
    internal class PuppetWorld : MonoBehaviour
    {
        private const float InterpolationDelay = 0.12f;
        private const float ClockSnap = 0.5f;
        private const int MaxBuffered = 20000;
        private const int MaxSamples = 6;
        private const float TeleportDistance = 2f;
        private const float CorrectDistance = 0.15f;
        private const float CorrectRate = 4f;
        private const float MinAliveHealth = 0.02f;

        private struct Sample
        {
            public float time;
            public Vector3 pos;
        }

        private class Puppet
        {
            public Agent agent;
            public bool native; // created by the local simulation (Viking on a seeded ship)
            public readonly List<Sample> samples = new List<Sample>(MaxSamples);

            public void Add(Sample s)
            {
                if (samples.Count > 0 && s.time <= samples[samples.Count - 1].time)
                    return;
                samples.Add(s);
                if (samples.Count > MaxSamples)
                    samples.RemoveAt(0);
            }
        }

        /// <summary>Set while applying a death the defender reported, so the damage patch lets it through.</summary>
        public static bool AuthorityKill { get; private set; }

        private static readonly Queue<byte[]> buffered = new Queue<byte[]>();
        private static readonly Dictionary<int, Squad> squads = new Dictionary<int, Squad>();
        private static readonly HashSet<Squad> upgradedSquads = new HashSet<Squad>();
        private static readonly Dictionary<int, Puppet> puppets = new Dictionary<int, Puppet>();
        private static readonly Dictionary<string, List<Agent>> nativeLandings = new Dictionary<string, List<Agent>>();
        private static readonly Dictionary<string, Agent> prefabs = new Dictionary<string, Agent>();
        private static readonly HashSet<string> warned = new HashSet<string>();
        private static List<KeyValuePair<float, byte[]>> replay;
        private static int replayIndex;
        private static float replayStart = -1f;
        private static float latestStreamTime = -1f;
        private static float renderTime = -1f;
        private static List<Agent> collecting;

        public static int PuppetCount => puppets.Count;

        private static bool Ready => SpectatorSession.Active && SpectatorSession.IslandReady();

        /// <summary>A battle message from the network (or replay). Held until the island has loaded.</summary>
        public static void Receive(byte[] packet)
        {
            if (Ready)
            {
                Handle(packet);
                return;
            }
            if ((MsgType)packet[0] != MsgType.Snapshot && buffered.Count < MaxBuffered)
                buffered.Enqueue(packet);
        }

        public static void SetReplay(List<KeyValuePair<float, byte[]>> records)
        {
            replay = records;
            replayIndex = 0;
            replayStart = -1f;
        }

        public static void Reset()
        {
            buffered.Clear();
            squads.Clear();
            upgradedSquads.Clear();
            puppets.Clear();
            nativeLandings.Clear();
            prefabs.Clear();
            warned.Clear();
            replay = null;
            latestStreamTime = -1f;
            renderTime = -1f;
            collecting = null;
        }

        // ---- Native Viking ships (Landing.Spawn on the spectator) -----------------------------

        public static void OnNativeLandingSpawnBegin() => collecting = new List<Agent>();

        public static void OnNativeAgentCreated(Agent agent) => collecting?.Add(agent);

        public static void OnNativeLandingSpawnEnd(Landing landing)
        {
            List<Agent> created = collecting;
            collecting = null;
            if (created != null && DefenderTracker.TryGetLandingKey(landing, out int wave, out int group, out int index))
                nativeLandings[$"{wave}/{group}/{index}"] = created;
        }

        // ---- Frame update --------------------------------------------------------------------

        private void Update()
        {
            if (!Ready)
                return;

            while (buffered.Count > 0)
                Handle(buffered.Dequeue());

            if (replay != null && replay.Count > 0)
            {
                if (replayStart < 0)
                    replayStart = Time.unscaledTime - replay[0].Key;
                float now = Time.unscaledTime - replayStart;
                while (replayIndex < replay.Count && replay[replayIndex].Key <= now)
                    Handle(replay[replayIndex++].Value);
            }

            AdvanceClock(Time.unscaledDeltaTime);
            if (renderTime >= 0)
                CorrectPositions(Time.deltaTime);
        }

        /// <summary>Render clock: runs in real time, kept InterpolationDelay behind the newest snapshot.</summary>
        private static void AdvanceClock(float dt)
        {
            if (latestStreamTime < 0)
                return;
            float target = latestStreamTime - InterpolationDelay;
            if (renderTime < 0 || Mathf.Abs(renderTime - target) > ClockSnap)
                renderTime = target;
            else
                renderTime += dt + (target - renderTime) * 0.05f;
        }

        /// <summary>Pull each unit toward where the defender has it. Small drift is walked off, large drift snaps.</summary>
        private static void CorrectPositions(float dt)
        {
            Island island = GameAccess.CurrentIsland;
            foreach (Puppet p in puppets.Values)
            {
                Agent agent = p.agent;
                if (!agent || !agent.gameObject.activeInHierarchy || !TargetAt(p, out Vector3 target))
                    continue;
                if (agent.deadState == null || agent.deadState.active || agent.aliveAndGrounded == null || !agent.aliveAndGrounded.active)
                    continue; // dying, ragdolling or still aboard a ship
                NavPos navPos = agent.navPos;
                if (navPos.navigationMesh != island.navMesh)
                    continue;

                Vector3 error = target - agent.wPos;
                error.y = 0f;
                float distance = error.magnitude;
                if (distance > TeleportDistance)
                {
                    agent.navPos = new NavPos(island.navMesh, target);
                    agent.transform.position = target;
                    agent.wPos = target;
                }
                else if (distance > CorrectDistance)
                {
                    navPos.pos += navPos.transform.InverseTransformVector(error) * Mathf.Min(1f, dt * CorrectRate);
                    agent.navPos = navPos;
                }
            }
        }

        private static bool TargetAt(Puppet p, out Vector3 pos)
        {
            pos = Vector3.zero;
            List<Sample> samples = p.samples;
            if (samples.Count == 0 || samples[0].time > renderTime)
                return false;
            int i = samples.Count - 1;
            while (i > 0 && samples[i].time > renderTime)
                i--;
            if (i == samples.Count - 1)
            {
                pos = samples[i].pos;
                return true;
            }
            Sample a = samples[i], b = samples[i + 1];
            pos = Vector3.Lerp(a.pos, b.pos, Mathf.InverseLerp(a.time, b.time, renderTime));
            return true;
        }

        // ---- Message handling ----------------------------------------------------------------

        private static void Handle(byte[] packet)
        {
            try
            {
                using (var r = new BinaryReader(new MemoryStream(packet)))
                {
                    switch ((MsgType)r.ReadByte())
                    {
                        case MsgType.SquadSpawn: OnSquadSpawn(r); break;
                        case MsgType.AgentSpawn: OnAgentSpawn(r); break;
                        case MsgType.AgentSpawned: OnAgentSpawned(r.ReadInt32()); break;
                        case MsgType.AgentDied: OnAgentDied(r.ReadInt32()); break;
                        case MsgType.AgentGone: OnAgentGone(r.ReadInt32()); break;
                        case MsgType.Snapshot: OnSnapshot(r); break;
                        case MsgType.HouseState: OnHouseState(r.ReadByte(), r.ReadByte()); break;
                        case MsgType.LandingSpawned: OnLandingSpawned(r); break;
                        case MsgType.SquadNavSpot: OnSquadNavSpot(r.ReadInt32(), r.ReadInt32()); break;
                        case MsgType.WaveLaunch: OnWaveLaunch(r.ReadInt32()); break;
                        case MsgType.BattlePhase: OnBattlePhase(r.ReadByte()); break;
                    }
                }
            }
            catch (Exception e)
            {
                WarnOnce("handle " + (MsgType)packet[0], $"Spectator couldn't handle {(MsgType)packet[0]}: {e}");
            }
        }

        private static void OnSquadSpawn(BinaryReader r)
        {
            int squadId = r.ReadInt32();
            byte side = r.ReadByte();
            int heroIndex = r.ReadInt32();
            Vector3 pos = r.ReadVector3();

            Island island = GameAccess.CurrentIsland;
            PrefabManager prefabManager = ScriptableObjectSingleton<PrefabManager>.instance;
            NavPos navPos = MakeNavPos(island, pos);
            if (side == 0)
            {
                // Same as SquadSpawner.SpawnEnglishSquad; agents arrive as AgentSpawn messages and upgrades
                // are applied once the hero (standard) exists, as in the original order.
                var squad = (EnglishSquad)prefabManager.englishSquad.SpawnGetFromPrefab(navPos, island.english);
                squad.squadTemplate = prefabManager.militiaTemplate;
                var heroes = Profile.campaign.heroes;
                if (heroIndex >= 0 && heroIndex < heroes.Count)
                    squad.hero = heroes[heroIndex];
                squads[squadId] = squad;
            }
            else
            {
                squads[squadId] = prefabManager.vikingSquad.SpawnGetFromPrefab(navPos, island.vikings);
            }
        }

        private static void OnAgentSpawn(BinaryReader r)
        {
            int agentId = r.ReadInt32();
            int squadId = r.ReadInt32();
            string prefabName = r.ReadString();
            bool isStandard = r.ReadBoolean();
            Vector3 pos = r.ReadVector3();

            Agent prefab = FindPrefab(prefabName);
            if (!prefab || !squads.TryGetValue(squadId, out Squad squad) || !squad)
            {
                WarnOnce("prefab " + prefabName, $"Can't mirror agent '{prefabName}' (prefab found: {(bool)prefab})");
                return;
            }

            Agent agent = squad.CreateAgent(prefab, MakeNavPos(squad.faction.island, pos));
            if (squad is EnglishSquad english)
            {
                if (isStandard)
                {
                    english.standard = agent.GetComponent<Standard>();
                    ApplyUpgrades(english);
                }
            }
            else
            {
                SetVikingReference(squad, agent, prefabName);
            }
            agent.gameObject.SetActive(false); // until the defender spawns it
            puppets[agentId] = new Puppet { agent = agent };
        }

        private static void ApplyUpgrades(EnglishSquad squad)
        {
            if (!upgradedSquads.Add(squad) || squad.hero == null)
                return;
            SquadUpgradeManager upgrades = squad.upgradeManager;
            foreach (UpgradeComponent upgrade in ScriptableObjectSingleton<PrefabManager>.instance.universalUpgrades)
                upgrades.AddUpgrade(upgrade, 0);
            foreach (SerializableHeroUpgrade upgrade in squad.hero.upgrades)
                upgrade.definition.OnAppliedToSquad(squad, upgrade.level);
        }

        private static void SetVikingReference(Squad squad, Agent agent, string prefabName)
        {
            VikingReference reference = squad.faction.island.levelNode.enemies.Find(v => v && v.agent && v.agent.name == prefabName);
            if (reference)
                agent.gameObject.GetOrAddComponent<VikingAgent>().vikingReference = reference;
        }

        private static void OnLandingSpawned(BinaryReader r)
        {
            string key = $"{r.ReadInt32()}/{r.ReadInt32()}/{r.ReadInt32()}";
            int count = r.ReadInt16();
            var ids = new int[count];
            for (int i = 0; i < count; i++)
                ids[i] = r.ReadInt32();

            if (!nativeLandings.TryGetValue(key, out List<Agent> natives) || natives.Count != count)
            {
                WarnOnce("landing " + key, $"Ship {key} doesn't match the defender's ({(natives == null ? "missing" : natives.Count + " vs " + count)} Vikings); its crew won't be corrected");
                return;
            }
            for (int i = 0; i < count; i++)
                puppets[ids[i]] = new Puppet { agent = natives[i], native = true };
        }

        private static void OnAgentSpawned(int id)
        {
            if (!puppets.TryGetValue(id, out Puppet p) || !p.agent || p.native)
                return; // native Vikings step off their ship by themselves
            p.agent.gameObject.SetActive(true);
            p.agent.Spawn();
        }

        private static void OnAgentDied(int id)
        {
            if (puppets.TryGetValue(id, out Puppet p) && p.agent && p.agent.deadState != null && !p.agent.deadState.active)
                Kill(p.agent);
        }

        /// <summary>The defender says this unit died: kill it through the game's own damage path.</summary>
        private static void Kill(Agent agent)
        {
            AuthorityKill = true;
            try
            {
                if (!agent.gameObject.activeInHierarchy)
                    agent.gameObject.SetActive(true);
                agent.DealDamage(new Attack(agent.health + 1000f, 0f, 0f, -agent.transform.forward, agent.wPos, null, null, "Sword"));
            }
            catch (Exception e)
            {
                WarnOnce("kill", $"Native kill failed, forcing death: {e.Message}");
            }
            finally
            {
                AuthorityKill = false;
            }
            if (agent && !agent.deadState.active)
            {
                agent.health = 0f;
                agent.deadState.SetActive(true);
                if (agent.squad)
                    agent.squad.ReportDead(agent);
            }
        }

        private static void OnAgentGone(int id)
        {
            if (!puppets.TryGetValue(id, out Puppet p))
                return;
            puppets.Remove(id);
            // Dead units finish dying locally (corpse); anything else was removed without dying (e.g. a restart).
            if (p.agent && (p.agent.deadState == null || !p.agent.deadState.active))
                Destroy(p.agent.gameObject);
        }

        private static void OnSnapshot(BinaryReader r)
        {
            float time = r.ReadSingle();
            latestStreamTime = Mathf.Max(latestStreamTime, time);
            int count = r.ReadInt16();
            for (int i = 0; i < count; i++)
            {
                int id = r.ReadInt32();
                Vector3 pos = r.ReadVector3();
                r.ReadInt16();                    // yaw: the local simulation faces its own way
                float health = r.ReadByte() / 255f;
                r.ReadInt32();                    // animation: played natively
                r.ReadUInt16();

                if (!puppets.TryGetValue(id, out Puppet p) || !p.agent)
                    continue;
                if (p.agent.deadState != null && !p.agent.deadState.active)
                    p.agent.health = Mathf.Max(health, MinAliveHealth) * p.agent.maxHealth;
                p.Add(new Sample { time = time, pos = pos });
            }
        }

        private static void OnSquadNavSpot(int squadId, int navSpotIndex)
        {
            if (!squads.TryGetValue(squadId, out Squad squad) || !(squad is EnglishSquad english) || !english.navSpotOccupant)
                return;
            NavSpot target = GameAccess.CurrentIsland.navSpotter.navSpots.Find(s => s && s.index == navSpotIndex);
            if (target && english.navSpotOccupant.navSpot != target)
                english.navSpotOccupant.SetNavSpot(target, showPath: false);
        }

        private static void OnWaveLaunch(int waveIndex)
        {
            Raid raid = GameAccess.CurrentIsland.raid;
            if (!raid || raid.waves == null || waveIndex < 0 || waveIndex >= raid.waves.Count || raid.waves[waveIndex].triggered)
                return;
            Traverse.Create(raid).Field<int>("waveIndex").Value = waveIndex;
            raid.BeginNextWave();
        }

        private static void OnBattlePhase(byte phase)
        {
            var manager = Singleton<IslandGameplayManager>.instance;
            if (phase == 1 && manager && !manager.states.running.active)
                manager.states.running.SetActive(true); // battle music and ambience
        }

        private static void OnHouseState(byte index, byte flags)
        {
            Island island = GameAccess.CurrentIsland;
            if (!island.village || index >= island.village.houses.Length)
                return;
            House house = island.village.houses[index];
            if ((flags & HouseFlags.Burning) != 0 && !house.burning.active)
                house.burning.SetActive(true);
            if ((flags & HouseFlags.Destroying) != 0 && !house.destroying.active)
                house.destroying.SetActive(true);
            if ((flags & HouseFlags.Pillaged) != 0 && !house.pillaged.active)
            {
                house.pillaged.SetActive(true);
                house.PaintSoot();
            }
        }

        // ---- Helpers -------------------------------------------------------------------------

        private static NavPos MakeNavPos(Island island, Vector3 worldPos)
        {
            var navPos = new NavPos(island.navMesh, worldPos);
            return navPos.valid ? navPos : island.navSpotter.navSpots[0].navPos;
        }

        /// <summary>Agent prefabs (and the game's template clones) are any Agent not part of a squad.</summary>
        private static Agent FindPrefab(string name)
        {
            if (prefabs.TryGetValue(name, out Agent prefab) && prefab)
                return prefab;
            prefabs.Clear();
            foreach (Agent candidate in Resources.FindObjectsOfTypeAll<Agent>())
            {
                if (candidate && !candidate.squad && !prefabs.ContainsKey(candidate.name))
                    prefabs[candidate.name] = candidate;
            }
            prefabs.TryGetValue(name, out prefab);
            return prefab;
        }

        private static void WarnOnce(string key, string message)
        {
            if (warned.Add(key))
                ModLog.Warn(message);
        }
    }

    // ---- Spectator-only patches that keep the local simulation under the defender's authority ----

    [HarmonyPatch(typeof(Agent), nameof(Agent.DealDamage))]
    internal static class SpectatorDamagePatch
    {
        // Hits still knock back, stagger and play effects, but only the defender decides who dies.
        private static void Prefix(ref Attack attack)
        {
            if (SpectatorSession.Active && !PuppetWorld.AuthorityKill)
                attack.damage = 0f;
        }
    }

    [HarmonyPatch(typeof(House), nameof(House.TorchLand))]
    internal static class SpectatorTorchLandPatch
    {
        // The torch still flies; whether the house burns comes from the defender.
        private static bool Prefix() => !SpectatorSession.Active;
    }

    [HarmonyPatch(typeof(House), nameof(House.TorchThrow))]
    internal static class SpectatorTorchThrowPatch
    {
        private static bool Prefix() => !SpectatorSession.Active;
    }

    [HarmonyPatch(typeof(Landing), nameof(Landing.Spawn))]
    internal static class SpectatorLandingSpawnPatch
    {
        private static void Prefix()
        {
            if (SpectatorSession.Active)
                PuppetWorld.OnNativeLandingSpawnBegin();
        }

        private static void Postfix(Landing __instance)
        {
            if (SpectatorSession.Active)
                PuppetWorld.OnNativeLandingSpawnEnd(__instance);
        }
    }

    [HarmonyPatch(typeof(Squad), nameof(Squad.CreateAgent))]
    internal static class SpectatorCreateAgentPatch
    {
        private static void Postfix(Agent __result)
        {
            if (SpectatorSession.Active)
                PuppetWorld.OnNativeAgentCreated(__result);
        }
    }

    [HarmonyPatch(typeof(SquadSelector), nameof(SquadSelector.SelectSquad), typeof(EnglishSquad), typeof(bool))]
    internal static class SpectatorSelectPatch
    {
        // The mastermind can't command the defender's squads.
        private static bool Prefix(EnglishSquad newSelection) => !(SpectatorSession.Active && newSelection);
    }
}
