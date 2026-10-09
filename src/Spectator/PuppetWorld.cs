using System;
using System.Collections.Generic;
using System.IO;
using BadNorthMP.Net;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Spectator
{
    /// <summary>
    /// Mastermind side: mirrors the defender's units and longships on the spectator island. Agents are created
    /// from the same prefabs through the game's own Squad.CreateAgent, but their AI never runs (AgentUpdater is
    /// skipped). Everything is rendered a little in the past (InterpolationDelay) so each frame can be
    /// interpolated between two real snapshots, including the exact animation frame.
    /// </summary>
    internal class PuppetWorld : MonoBehaviour
    {
        private const float InterpolationDelay = 0.12f;
        private const float ClockSnap = 0.5f;
        private const int MaxBuffered = 20000;
        private const int MaxSamples = 6;

        private struct Sample
        {
            public float time;
            public Vector3 pos;
            public Quaternion rot;
            public int anim;
            public float animTime;
            public bool moving;
            public float speed;
        }

        private class Mirror
        {
            public readonly List<Sample> samples = new List<Sample>(MaxSamples);
            public Animator animator;

            public void Add(Sample s)
            {
                if (samples.Count > 0 && s.time <= samples[samples.Count - 1].time)
                    return; // out of order / duplicate
                samples.Add(s);
                if (samples.Count > MaxSamples)
                    samples.RemoveAt(0);
            }
        }

        private class Puppet : Mirror
        {
            public Agent agent;
            public Vector3 spawnPos;
        }

        private class ShipPuppet : Mirror
        {
            public GameObject go;
        }

        private static readonly Queue<byte[]> buffered = new Queue<byte[]>();
        private static readonly Dictionary<int, Squad> squads = new Dictionary<int, Squad>();
        private static readonly Dictionary<int, Puppet> puppets = new Dictionary<int, Puppet>();
        private static readonly Dictionary<int, ShipPuppet> shipPuppets = new Dictionary<int, ShipPuppet>();
        private static readonly Dictionary<int, ShipPuppet> projectilePuppets = new Dictionary<int, ShipPuppet>();
        private static readonly Dictionary<string, GameObject> projectilePrefabs = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, ReusableEffect> effects = new Dictionary<string, ReusableEffect>();
        private static readonly Dictionary<string, Agent> prefabs = new Dictionary<string, Agent>();
        private static readonly HashSet<string> warned = new HashSet<string>();
        private static List<KeyValuePair<float, byte[]>> replay;
        private static int replayIndex;
        private static float replayStart = -1f;
        private static float latestStreamTime = -1f;
        private static float renderTime = -1f;

        public static int PuppetCount => puppets.Count;
        public static int ShipCount => shipPuppets.Count;

        private static bool Ready => SpectatorSession.Active && SpectatorSession.IslandReady();

        /// <summary>A battle message from the network (or replay). Held until the island has loaded.</summary>
        public static void Receive(byte[] packet)
        {
            if (Ready)
            {
                Handle(packet);
                return;
            }
            // Transforms are stale by the time the island loads; only keep the events.
            var type = (MsgType)packet[0];
            bool transient = type == MsgType.Snapshot || type == MsgType.ShipSnapshot || type == MsgType.ProjectileSnapshot
                || type == MsgType.Sound || type == MsgType.Effect;
            if (!transient && buffered.Count < MaxBuffered)
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
            puppets.Clear();
            shipPuppets.Clear();
            projectilePuppets.Clear();
            projectilePrefabs.Clear();
            effects.Clear();
            prefabs.Clear();
            warned.Clear();
            replay = null;
            latestStreamTime = -1f;
            renderTime = -1f;
        }

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
            if (renderTime < 0)
                return;
            foreach (Puppet p in puppets.Values)
                ApplyAgent(p);
            foreach (ShipPuppet s in shipPuppets.Values)
                ApplyShip(s);
            foreach (ShipPuppet s in projectilePuppets.Values)
                ApplyShip(s);
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
                renderTime += dt + (target - renderTime) * 0.05f; // drift gently toward the target
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
                        case MsgType.AgentGone: OnAgentGone(r.ReadInt32(), r.ReadBoolean()); break;
                        case MsgType.Snapshot: OnSnapshot(r); break;
                        case MsgType.HouseState: OnHouseState(r.ReadByte(), r.ReadByte(), r.ReadUInt16() / 1000f); break;
                        case MsgType.Dust: Singleton<DustParticles>.instance.SpawnParticles(r.ReadVector3(), r.ReadVector3()); break;
                        case MsgType.ShipSpawn: OnShipSpawn(r.ReadInt32(), r.ReadString()); break;
                        case MsgType.ShipGone: OnShipGone(r.ReadInt32()); break;
                        case MsgType.ShipSnapshot: OnShipSnapshot(r); break;
                        case MsgType.ProjectileSpawn: OnProjectileSpawn(r.ReadInt32(), r.ReadString()); break;
                        case MsgType.ProjectileGone: OnProjectileGone(r.ReadInt32()); break;
                        case MsgType.ProjectileSnapshot: OnProjectileSnapshot(r); break;
                        case MsgType.Sound: OnSound(r); break;
                        case MsgType.Effect: OnEffect(r); break;
                    }
                }
            }
            catch (Exception e)
            {
                WarnOnce("handle " + (MsgType)packet[0], $"Puppet world couldn't handle {(MsgType)packet[0]}: {e}");
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
            if (isStandard && squad is EnglishSquad english)
                english.standard = agent.GetComponent<Standard>();
            if (squad.faction.side != Faction.Side.English)
            {
                // Landing.Spawn sets this; VikingSquadDifficulty and others read it when the agent spawns.
                VikingReference reference = squad.faction.island.levelNode.enemies.Find(v => v && v.agent && v.agent.name == prefabName);
                if (reference)
                    agent.gameObject.GetOrAddComponent<VikingAgent>().vikingReference = reference;
                else
                    WarnOnce("vikingref " + prefabName, $"No VikingReference for '{prefabName}'");
            }
            // Hidden until it shows up in a snapshot (Vikings appear when their ship sails in).
            agent.gameObject.SetActive(false);
            puppets[agentId] = new Puppet { agent = agent, animator = agent.animator, spawnPos = pos };
        }

        private static void OnAgentSpawned(int id)
        {
            if (puppets.TryGetValue(id, out Puppet p) && p.agent)
            {
                p.agent.gameObject.SetActive(true);
                p.agent.Spawn();
            }
        }

        private static void OnAgentDied(int id)
        {
            if (puppets.TryGetValue(id, out Puppet p) && p.agent && p.agent.deadState != null)
                p.agent.deadState.SetActive(true);
        }

        private static void OnAgentGone(int id, bool died)
        {
            if (!puppets.TryGetValue(id, out Puppet p))
                return;
            puppets.Remove(id);
            if (!p.agent)
                return;

            Death death = died ? p.agent.GetComponent<Death>() : null;
            if (death && p.agent.gameObject.activeInHierarchy)
            {
                try
                {
                    death.Die(); // corpse, blood and FinalDeath, like the real thing
                    return;
                }
                catch (Exception e)
                {
                    WarnOnce("death", $"Puppet death failed, removing instead: {e.Message}");
                }
            }
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
                Quaternion rot = r.ReadRotation();
                float health = r.ReadByte() / 255f;
                int anim = r.ReadInt32();
                float animTime = r.ReadUInt16() / 65535f;
                bool moving = r.ReadByte() != 0;
                float speed = r.ReadUInt16() / 100f;

                if (!puppets.TryGetValue(id, out Puppet p) || !p.agent)
                    continue;
                p.agent.health = health * p.agent.maxHealth;
                p.Add(new Sample { time = time, pos = pos, rot = rot, anim = anim, animTime = animTime, moving = moving, speed = speed });
            }
        }

        private static void OnShipSpawn(int id, string prefabName)
        {
            Island island = GameAccess.CurrentIsland;
            Longship prefab = island.levelNode.possibleShips.Find(s => s && s.name == prefabName);
            if (!prefab)
            {
                WarnOnce("ship " + prefabName, $"Can't mirror longship '{prefabName}'");
                return;
            }

            GameObject go = Instantiate(prefab.gameObject, island.runContainer);
            go.SetActive(false);
            // A visual copy only: no sailing logic, no nav mesh, no collisions.
            var longship = go.GetComponent<Longship>();
            if (longship.navMeshFilter && longship.navMeshFilter.GetComponent<MeshRenderer>())
                longship.navMeshFilter.GetComponent<MeshRenderer>().enabled = false;
            if (longship.navigationMesh)
                longship.navigationMesh.enabled = false;
            longship.enabled = false;
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                c.enabled = false;
            ExtraRenderers.InitializeAll(go.transform);
            shipPuppets[id] = new ShipPuppet { go = go, animator = go.GetComponent<Animator>() };
        }

        private static void OnShipGone(int id)
        {
            if (shipPuppets.TryGetValue(id, out ShipPuppet s))
            {
                shipPuppets.Remove(id);
                if (s.go)
                    Destroy(s.go);
            }
        }

        private static void OnShipSnapshot(BinaryReader r)
        {
            float time = r.ReadSingle();
            latestStreamTime = Mathf.Max(latestStreamTime, time);
            int count = r.ReadInt16();
            for (int i = 0; i < count; i++)
            {
                int id = r.ReadInt32();
                Vector3 pos = r.ReadVector3();
                var rot = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                int anim = r.ReadInt32();
                float animTime = r.ReadUInt16() / 65535f;
                if (shipPuppets.TryGetValue(id, out ShipPuppet s) && s.go)
                    s.Add(new Sample { time = time, pos = pos, rot = rot, anim = anim, animTime = animTime });
            }
        }

        // ---- Projectiles: visual copies following the defender's flight path ------------------

        private static void OnProjectileSpawn(int id, string prefabName)
        {
            GameObject prefab = FindProjectilePrefab(prefabName);
            if (!prefab)
            {
                WarnOnce("projectile " + prefabName, $"Can't mirror projectile '{prefabName}'");
                return;
            }
            GameObject go = Instantiate(prefab, GameAccess.CurrentIsland.runContainer);
            go.SetActive(false);
            // Visual only: no flight logic, pooling, physics or hits.
            foreach (MonoBehaviour behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                c.enabled = false;
            foreach (Rigidbody body in go.GetComponentsInChildren<Rigidbody>(true))
                body.isKinematic = true;
            projectilePuppets[id] = new ShipPuppet { go = go };
        }

        private static void OnProjectileGone(int id)
        {
            if (projectilePuppets.TryGetValue(id, out ShipPuppet p))
            {
                projectilePuppets.Remove(id);
                if (p.go)
                    Destroy(p.go);
            }
        }

        private static void OnProjectileSnapshot(BinaryReader r)
        {
            float time = r.ReadSingle();
            latestStreamTime = Mathf.Max(latestStreamTime, time);
            int count = r.ReadInt16();
            for (int i = 0; i < count; i++)
            {
                int id = r.ReadInt32();
                Vector3 pos = r.ReadVector3();
                Quaternion rot = r.ReadRotation();
                if (projectilePuppets.TryGetValue(id, out ShipPuppet p) && p.go)
                    p.Add(new Sample { time = time, pos = pos, rot = rot });
            }
        }

        private static GameObject FindProjectilePrefab(string name)
        {
            if (projectilePrefabs.TryGetValue(name, out GameObject found) && found)
                return found;
            // Templates are the inactive, unparented pool sources (and prefab assets) with this exact name.
            foreach (SelfPoolingPrefab candidate in Resources.FindObjectsOfTypeAll<SelfPoolingPrefab>())
                if (candidate && candidate.name == name) { found = candidate.gameObject; break; }
            if (!found)
                foreach (PoolablePrefab candidate in Resources.FindObjectsOfTypeAll<PoolablePrefab>())
                    if (candidate && candidate.name == name) { found = candidate.gameObject; break; }
            projectilePrefabs[name] = found;
            return found;
        }

        // ---- Sounds and effects ----------------------------------------------------------------

        private static readonly List<KeyValuePair<float, GameObject>> soundAnchors = new List<KeyValuePair<float, GameObject>>();

        private static void OnSound(BinaryReader r)
        {
            byte target = r.ReadByte();
            int targetId = r.ReadInt32();
            Vector3 pos = r.ReadVector3();
            bool byName = r.ReadBoolean();
            string name = byName ? r.ReadString() : null;
            int eventId = byName ? 0 : r.ReadInt32();
            int action = r.ReadInt32();

            GameObject parent = null;
            switch (target)
            {
                case SoundTarget.Agent:
                    if (puppets.TryGetValue(targetId, out Puppet p) && p.agent)
                        parent = p.agent.gameObject;
                    break;
                case SoundTarget.Ship:
                    if (shipPuppets.TryGetValue(targetId, out ShipPuppet s) && s.go)
                        parent = s.go;
                    break;
                case SoundTarget.House:
                    Island island = GameAccess.CurrentIsland;
                    if (island.village && targetId < island.village.houses.Length)
                        parent = island.village.houses[targetId].gameObject;
                    break;
            }
            if (!parent && target != SoundTarget.Global)
                parent = SoundAnchor(pos);

            if (parent)
            {
                if (byName)
                    FabricWrapper.PostEvent(name, action < 0 ? Fabric.EventAction.PlaySound : (Fabric.EventAction)action, parent);
                else
                    FabricWrapper.PostEvent(eventId, action < 0 ? Fabric.EventAction.PlaySound : (Fabric.EventAction)action, parent);
            }
            else if (byName)
            {
                if (action < 0) FabricWrapper.PostEvent(name);
                else FabricWrapper.PostEvent(name, (Fabric.EventAction)action);
            }
        }

        /// <summary>A short-lived object to play a positional sound from, when it has no mirrored owner.</summary>
        private static GameObject SoundAnchor(Vector3 pos)
        {
            for (int i = soundAnchors.Count - 1; i >= 0; i--)
            {
                if (soundAnchors[i].Key < Time.unscaledTime)
                {
                    if (soundAnchors[i].Value)
                        Destroy(soundAnchors[i].Value);
                    soundAnchors.RemoveAt(i);
                }
            }
            var anchor = new GameObject("BNMP Sound");
            anchor.transform.position = pos;
            soundAnchors.Add(new KeyValuePair<float, GameObject>(Time.unscaledTime + 10f, anchor));
            return anchor;
        }

        private static void OnEffect(BinaryReader r)
        {
            string name = r.ReadString();
            Vector3 pos = r.ReadVector3();
            Quaternion rot = r.ReadRotation();
            Vector3 scale = r.ReadVector3();
            bool onNavMesh = r.ReadBoolean();

            if (!effects.TryGetValue(name, out ReusableEffect effect) || !effect)
            {
                effect = null;
                foreach (ReusableEffect candidate in Resources.FindObjectsOfTypeAll<ReusableEffect>())
                    if (candidate && candidate.name == name) { effect = candidate; break; }
                effects[name] = effect;
                if (!effect)
                    WarnOnce("effect " + name, $"Can't mirror effect '{name}'");
            }
            if (!effect)
                return;

            Island island = GameAccess.CurrentIsland;
            var navPos = onNavMesh ? new NavPos(island.navMesh, pos) : default(NavPos);
            if (onNavMesh && navPos.valid)
                effect.PlayAt(navPos, rot, scale); // also stamps ground decals (blood)
            else
                effect.PlayAt(pos, rot, scale);
        }

        private static void OnHouseState(byte index, byte flags, float fire)
        {
            Island island = GameAccess.CurrentIsland;
            if (!island.village || index >= island.village.houses.Length)
                return;
            House house = island.village.houses[index];
            // The house's own update turns this into flames, smoke, light and fire audio.
            Traverse.Create(house).Property("fire").SetValue(fire);
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

        // ---- Per-frame -----------------------------------------------------------------------

        private static void ApplyAgent(Puppet p)
        {
            if (!p.agent || !SampleAt(p, out Sample s))
                return;
            if (!p.agent.gameObject.activeSelf)
                p.agent.gameObject.SetActive(true);
            if (p.agent.deadState != null && p.agent.deadState.active)
                return; // the native death animation plays from where it fell
            Transform tr = p.agent.transform;
            tr.position = s.pos;
            tr.rotation = s.rot;
            p.agent.wPos = s.pos;
            PlayFrame(p.animator, s, agentParams: true);
        }

        private static void ApplyShip(ShipPuppet ship)
        {
            if (!ship.go || !SampleAt(ship, out Sample s))
                return;
            if (!ship.go.activeSelf)
                ship.go.SetActive(true);
            ship.go.transform.position = s.pos;
            ship.go.transform.rotation = s.rot;
            PlayFrame(ship.animator, s);
        }

        /// <summary>The mirror's state at renderTime, interpolated between the two snapshots around it.</summary>
        private static bool SampleAt(Mirror m, out Sample result)
        {
            result = default(Sample);
            List<Sample> samples = m.samples;
            if (samples.Count == 0 || samples[0].time > renderTime)
                return false;

            int i = samples.Count - 1;
            while (i > 0 && samples[i].time > renderTime)
                i--;
            Sample a = samples[i];
            if (i == samples.Count - 1)
            {
                result = a; // newest data is older than the render time: hold it
                return true;
            }
            Sample b = samples[i + 1];
            float t = Mathf.InverseLerp(a.time, b.time, renderTime);
            result.time = renderTime;
            result.pos = Vector3.Lerp(a.pos, b.pos, t);
            result.rot = Quaternion.Slerp(a.rot, b.rot, t);
            result.moving = t < 0.5f ? a.moving : b.moving;
            result.speed = Mathf.Lerp(a.speed, b.speed, t);
            if (a.anim == b.anim)
            {
                float end = b.animTime < a.animTime ? b.animTime + 1f : b.animTime; // looped around
                result.anim = a.anim;
                result.animTime = Mathf.Min(Mathf.Lerp(a.animTime, end, t) % 1f, 0.999f);
            }
            else
            {
                result.anim = t < 0.5f ? a.anim : b.anim;
                result.animTime = t < 0.5f ? a.animTime : b.animTime;
            }
            return true;
        }

        private static readonly int MovingParam = Animator.StringToHash("Moving");
        private static readonly int SpeedParam = Animator.StringToHash("Speed");

        private static void PlayFrame(Animator animator, Sample s, bool agentParams = false)
        {
            if (s.anim == 0 || !animator || !animator.isActiveAndEnabled)
                return;
            if (agentParams)
            {
                // Without these the controller transitions straight back out of Walk.
                animator.SetBool(MovingParam, s.moving);
                animator.SetFloat(SpeedParam, s.speed);
            }
            animator.Play(s.anim, 0, Mathf.Min(s.animTime, 0.999f)); // see DefenderTracker.WriteAnimation
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

    // Animation events on the puppets' clips would run game actions (spawning a held torch, throwing,
    // attack callbacks). The defender's real actions arrive as mirrored projectiles and effects instead.
    [HarmonyPatch(typeof(Agent), nameof(Agent.StartThrow))]
    internal static class SpectatorStartThrowPatch
    {
        private static bool Prefix() => !SpectatorSession.Active;
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.Throw))]
    internal static class SpectatorThrowPatch
    {
        private static bool Prefix() => !SpectatorSession.Active;
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.DoAnimationAction))]
    internal static class SpectatorAnimationActionPatch
    {
        private static bool Prefix() => !SpectatorSession.Active;
    }

    [HarmonyPatch(typeof(AgentUpdater), "Update")]
    internal static class SpectatorAgentUpdaterPatch
    {
        // All agent AI, movement, combat and push-apart physics run from here; puppets are driven instead.
        private static bool Prefix() => !SpectatorSession.Active;
    }

    [HarmonyPatch(typeof(SquadSelector), nameof(SquadSelector.SelectSquad), typeof(EnglishSquad), typeof(bool))]
    internal static class SpectatorSelectPatch
    {
        // The mastermind can't command the defender's squads (selecting would also slow time).
        private static bool Prefix(EnglishSquad newSelection) => !(SpectatorSession.Active && newSelection);
    }
}
