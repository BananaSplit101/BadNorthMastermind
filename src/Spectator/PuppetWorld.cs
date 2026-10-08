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
    /// Mastermind side: mirrors the defender's units on the spectator island. Squads and agents are created
    /// from the same prefabs through the game's own Squad.CreateAgent, but their AI never runs (AgentUpdater is
    /// skipped); instead each puppet follows the defender's position/facing/animation snapshots.
    /// </summary>
    internal class PuppetWorld : MonoBehaviour
    {
        private const float FollowSharpness = 12f;
        private const float SnapDistance = 3f;
        private const int MaxBuffered = 20000;

        private class Puppet
        {
            public Agent agent;
            public bool hasTarget;
            public Vector3 targetPos;
            public float targetYaw;
            public int anim;
        }

        private static readonly Queue<byte[]> buffered = new Queue<byte[]>();
        private static readonly Dictionary<int, Squad> squads = new Dictionary<int, Squad>();
        private static readonly Dictionary<int, Puppet> puppets = new Dictionary<int, Puppet>();
        private static readonly Dictionary<string, Agent> prefabs = new Dictionary<string, Agent>();
        private static List<KeyValuePair<float, byte[]>> replay;
        private static int replayIndex;
        private static float replayStart = -1f;
        private static readonly HashSet<string> warned = new HashSet<string>();

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
            // Positions are stale by the time the island loads; only keep the events.
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
            puppets.Clear();
            prefabs.Clear();
            warned.Clear();
            replay = null;
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

            Follow(Time.unscaledDeltaTime);
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
                        case MsgType.HouseState: OnHouseState(r.ReadByte(), r.ReadByte()); break;
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
            // Hidden until the defender's agent is actually spawned (Vikings wait on their ships).
            agent.gameObject.SetActive(false);
            puppets[agentId] = new Puppet { agent = agent, targetPos = pos };
        }

        private static void OnAgentSpawned(int id)
        {
            if (!puppets.TryGetValue(id, out Puppet p) || !p.agent)
                return;
            p.agent.gameObject.SetActive(true);
            p.agent.transform.position = p.targetPos;
            p.agent.Spawn();
        }

        private static void OnAgentDied(int id)
        {
            if (puppets.TryGetValue(id, out Puppet p) && p.agent && p.agent.deadState != null)
                p.agent.deadState.SetActive(true); // plays the death animation
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
            int count = r.ReadInt16();
            for (int i = 0; i < count; i++)
            {
                int id = r.ReadInt32();
                Vector3 pos = r.ReadVector3();
                float yaw = r.ReadInt16() / 100f;
                float health = r.ReadByte() / 255f;
                int anim = r.ReadInt32();
                float animTime = r.ReadByte() / 255f;

                if (!puppets.TryGetValue(id, out Puppet p) || !p.agent)
                    continue;
                if (!p.hasTarget)
                {
                    p.agent.transform.position = pos;
                    p.agent.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                }
                p.hasTarget = true;
                p.targetPos = pos;
                p.targetYaw = yaw;
                p.agent.health = health * p.agent.maxHealth;

                if (anim != 0 && anim != p.anim && p.agent.animator && p.agent.animator.HasState(0, anim))
                {
                    p.agent.animator.Play(anim, 0, animTime);
                    p.anim = anim;
                }
            }
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

        // ---- Per-frame -----------------------------------------------------------------------

        private static void Follow(float dt)
        {
            float t = 1f - Mathf.Exp(-dt * FollowSharpness);
            foreach (Puppet p in puppets.Values)
            {
                if (!p.hasTarget || !p.agent || !p.agent.gameObject.activeInHierarchy)
                    continue;
                if (p.agent.deadState != null && p.agent.deadState.active)
                    continue; // let the death animation play where it fell
                Transform tr = p.agent.transform;
                tr.position = (tr.position - p.targetPos).sqrMagnitude > SnapDistance * SnapDistance
                    ? p.targetPos
                    : Vector3.Lerp(tr.position, p.targetPos, t);
                tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.Euler(0f, p.targetYaw, 0f), t);
                p.agent.wPos = tr.position;
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
