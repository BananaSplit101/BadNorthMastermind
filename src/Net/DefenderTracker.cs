using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Defender side: gives every squad, agent and longship a network id as it is created, and streams what
    /// the mastermind needs to mirror them: creation, spawn, death, removal, 20 Hz transform/animation
    /// snapshots and house fire states. State changes are found by polling at the snapshot rate.
    /// </summary>
    internal static class DefenderTracker
    {
        private const float SnapshotInterval = 0.05f;
        private const int MaxPacketBytes = 1100; // Steam unreliable P2P packets max out around 1200 bytes
        private const int AgentRecordBytes = 4 + 12 + 2 + 1 + 4 + 2;
        private const int ShipRecordBytes = 4 + 12 + 16 + 4 + 2;

        private class TrackedAgent
        {
            public int id;
            public Agent agent;
            public bool spawned, died;
        }

        private class TrackedShip
        {
            public int id;
            public Longship ship;
            public Animator animator;
        }

        private static readonly Dictionary<Squad, int> squadIds = new Dictionary<Squad, int>();
        private static readonly HashSet<Agent> known = new HashSet<Agent>();
        private static readonly List<TrackedAgent> agents = new List<TrackedAgent>();
        private static readonly List<TrackedShip> ships = new List<TrackedShip>();
        private static byte[] houseFlags = new byte[0];
        private static int nextId = 1;
        private static float nextSnapshot;

        public static void Reset()
        {
            squadIds.Clear();
            known.Clear();
            agents.Clear();
            ships.Clear();
            houseFlags = new byte[0];
            nextId = 1;
        }

        public static void OnAgentCreated(Squad squad, Agent prefab, Agent agent)
        {
            if (!BattleStream.Active || !agent || !known.Add(agent))
                return;

            if (!squadIds.TryGetValue(squad, out int squadId))
            {
                squadId = squadIds[squad] = nextId++;
                var english = squad as EnglishSquad;
                int heroIndex = english && english.hero != null ? Profile.campaign.heroes.IndexOf(english.hero) : -1;
                byte side = (byte)(squad.faction && squad.faction.side == Faction.Side.English ? 0 : 1);
                Vector3 pos = squad.transform.position;
                BattleStream.Send(Packet.Build(MsgType.SquadSpawn, w =>
                {
                    w.Write(squadId);
                    w.Write(side);
                    w.Write(heroIndex);
                    w.Write(pos);
                }), reliable: true);
            }

            var entry = new TrackedAgent { id = nextId++, agent = agent };
            agents.Add(entry);
            bool isStandard = agent.GetComponent<Standard>();
            BattleStream.Send(Packet.Build(MsgType.AgentSpawn, w =>
            {
                w.Write(entry.id);
                w.Write(squadId);
                w.Write(prefab.name);
                w.Write(isStandard);
                w.Write(agent.transform.position);
            }), reliable: true);
        }

        public static void OnShipSpawned(Landing landing, Longship ship)
        {
            if (!BattleStream.Active || !ship || !landing.shipPrefab)
                return;
            var entry = new TrackedShip { id = nextId++, ship = ship, animator = ship.GetComponent<Animator>() };
            ships.Add(entry);
            string prefabName = landing.shipPrefab.name;
            BattleStream.Send(Packet.Build(MsgType.ShipSpawn, w =>
            {
                w.Write(entry.id);
                w.Write(prefabName);
            }), reliable: true);
        }

        public static void Update()
        {
            if (!BattleStream.Active || Time.unscaledTime < nextSnapshot)
                return;
            nextSnapshot = Time.unscaledTime + SnapshotInterval;
            float time = BattleStream.StreamTime;

            var visibleAgents = new List<TrackedAgent>();
            for (int i = agents.Count - 1; i >= 0; i--)
            {
                TrackedAgent t = agents[i];
                if (!t.agent)
                {
                    BattleStream.Send(Packet.Build(MsgType.AgentGone, w =>
                    {
                        w.Write(t.id);
                        w.Write(t.died);
                    }), reliable: true);
                    agents.RemoveAt(i);
                    continue;
                }
                if (!t.spawned && t.agent.spawned != null && t.agent.spawned.active)
                {
                    t.spawned = true;
                    BattleStream.Send(Packet.Build(MsgType.AgentSpawned, w => w.Write(t.id)), reliable: true);
                }
                if (!t.died && t.agent.deadState != null && t.agent.deadState.active)
                {
                    t.died = true;
                    BattleStream.Send(Packet.Build(MsgType.AgentDied, w => w.Write(t.id)), reliable: true);
                }
                // Includes Vikings riding in on their ship before they spawn onto the beach.
                if (t.agent.gameObject.activeInHierarchy)
                    visibleAgents.Add(t);
            }
            SendInPackets(MsgType.Snapshot, time, visibleAgents, AgentRecordBytes, WriteAgent);

            var visibleShips = new List<TrackedShip>();
            for (int i = ships.Count - 1; i >= 0; i--)
            {
                TrackedShip s = ships[i];
                if (!s.ship)
                {
                    BattleStream.Send(Packet.Build(MsgType.ShipGone, w => w.Write(s.id)), reliable: true);
                    ships.RemoveAt(i);
                    continue;
                }
                if (s.ship.gameObject.activeInHierarchy)
                    visibleShips.Add(s);
            }
            SendInPackets(MsgType.ShipSnapshot, time, visibleShips, ShipRecordBytes, WriteShip);

            SendHouseChanges();
        }

        private static void SendInPackets<T>(MsgType type, float time, List<T> items, int recordBytes, System.Action<BinaryWriter, T> write)
        {
            int perPacket = (MaxPacketBytes - 7) / recordBytes;
            for (int start = 0; start < items.Count; start += perPacket)
            {
                int count = Mathf.Min(perPacket, items.Count - start);
                int first = start;
                BattleStream.Send(Packet.Build(type, w =>
                {
                    w.Write(time);
                    w.Write((short)count);
                    for (int i = first; i < first + count; i++)
                        write(w, items[i]);
                }), reliable: false);
            }
        }

        private static void WriteAgent(BinaryWriter w, TrackedAgent t)
        {
            Agent agent = t.agent;
            w.Write(t.id);
            w.Write(agent.transform.position);
            Vector3 forward = agent.transform.forward;
            w.Write((short)Mathf.RoundToInt(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg * 100f));
            w.Write((byte)Mathf.RoundToInt(Mathf.Clamp01(agent.healthFraction) * 255f));
            WriteAnimation(w, agent.animator);
        }

        private static void WriteShip(BinaryWriter w, TrackedShip s)
        {
            w.Write(s.id);
            w.Write(s.ship.transform.position);
            Quaternion rot = s.ship.transform.rotation;
            w.Write(rot.x);
            w.Write(rot.y);
            w.Write(rot.z);
            w.Write(rot.w);
            WriteAnimation(w, s.animator);
        }

        /// <summary>
        /// The exact animation frame. Bad North drives walking by re-playing the walk state at the current
        /// step phase every frame (Body.SteppingUpdate), so the state alone isn't enough.
        /// </summary>
        private static void WriteAnimation(BinaryWriter w, Animator animator)
        {
            int anim = 0;
            float time = 0f;
            if (animator && animator.isActiveAndEnabled)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                anim = state.fullPathHash;
                time = state.loop ? state.normalizedTime % 1f : Mathf.Clamp01(state.normalizedTime);
            }
            w.Write(anim);
            w.Write((ushort)Mathf.RoundToInt(time * 65535f));
        }

        private static void SendHouseChanges()
        {
            Island island = GameAccess.CurrentIsland;
            if (!island || !island.village || island.village.houses == null)
                return;
            House[] houses = island.village.houses;
            if (houseFlags.Length != houses.Length)
                houseFlags = new byte[houses.Length];
            for (int i = 0; i < houses.Length && i < 256; i++)
            {
                House house = houses[i];
                if (!house)
                    continue;
                byte flags = 0;
                if (house.burning != null && house.burning.active) flags |= HouseFlags.Burning;
                if (house.destroying != null && house.destroying.active) flags |= HouseFlags.Destroying;
                if (house.pillaged != null && house.pillaged.active) flags |= HouseFlags.Pillaged;
                if (flags == houseFlags[i])
                    continue;
                houseFlags[i] = flags;
                byte index = (byte)i;
                BattleStream.Send(Packet.Build(MsgType.HouseState, w =>
                {
                    w.Write(index);
                    w.Write(flags);
                }), reliable: true);
            }
        }
    }

    [HarmonyPatch(typeof(Squad), nameof(Squad.CreateAgent))]
    internal static class DefenderCreateAgentPatch
    {
        private static void Postfix(Squad __instance, Agent prefab, Agent __result)
        {
            if (!Spectator.SpectatorSession.Active)
                DefenderTracker.OnAgentCreated(__instance, prefab, __result);
        }
    }

    [HarmonyPatch(typeof(Landing), nameof(Landing.Spawn))]
    internal static class DefenderLandingSpawnPatch
    {
        private static void Postfix(Landing __instance, Longship __result)
        {
            if (!Spectator.SpectatorSession.Active)
                DefenderTracker.OnShipSpawned(__instance, __result);
        }
    }
}
