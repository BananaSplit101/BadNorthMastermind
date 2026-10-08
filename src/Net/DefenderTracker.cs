using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Defender side: gives every squad and agent a network id as it is created, and streams what the
    /// mastermind needs to mirror them: creation, spawn, death, removal, positions/animation and houses.
    /// State changes are found by polling at the snapshot rate rather than by patching each code path.
    /// </summary>
    internal static class DefenderTracker
    {
        private const float SnapshotInterval = 0.1f;
        private const int MaxSnapshotBytes = 1100; // Steam unreliable P2P packets max out around 1200 bytes
        private const int SnapshotRecordBytes = 4 + 12 + 2 + 1 + 4 + 1;

        private class Tracked
        {
            public int id;
            public Agent agent;
            public bool spawned, died;
        }

        private static readonly Dictionary<Squad, int> squadIds = new Dictionary<Squad, int>();
        private static readonly Dictionary<Agent, Tracked> byAgent = new Dictionary<Agent, Tracked>();
        private static readonly List<Tracked> tracked = new List<Tracked>();
        private static byte[] houseFlags = new byte[0];
        private static int nextId = 1;
        private static float nextSnapshot;

        public static void Reset()
        {
            squadIds.Clear();
            byAgent.Clear();
            tracked.Clear();
            houseFlags = new byte[0];
            nextId = 1;
        }

        public static void OnAgentCreated(Squad squad, Agent prefab, Agent agent)
        {
            if (!BattleStream.Active || !agent || byAgent.ContainsKey(agent))
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

            var entry = new Tracked { id = nextId++, agent = agent };
            byAgent[agent] = entry;
            tracked.Add(entry);
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

        public static void Update()
        {
            if (!BattleStream.Active || Time.unscaledTime < nextSnapshot)
                return;
            nextSnapshot = Time.unscaledTime + SnapshotInterval;

            var visible = new List<Tracked>();
            for (int i = tracked.Count - 1; i >= 0; i--)
            {
                Tracked t = tracked[i];
                if (!t.agent)
                {
                    BattleStream.Send(Packet.Build(MsgType.AgentGone, w =>
                    {
                        w.Write(t.id);
                        w.Write(t.died);
                    }), reliable: true);
                    tracked.RemoveAt(i);
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
                if (t.spawned && !t.died)
                    visible.Add(t);
            }

            SendSnapshots(visible);
            SendHouseChanges();
        }

        private static void SendSnapshots(List<Tracked> visible)
        {
            int perPacket = (MaxSnapshotBytes - 3) / SnapshotRecordBytes;
            for (int start = 0; start < visible.Count; start += perPacket)
            {
                int count = Mathf.Min(perPacket, visible.Count - start);
                int first = start;
                BattleStream.Send(Packet.Build(MsgType.Snapshot, w =>
                {
                    w.Write((short)count);
                    for (int i = first; i < first + count; i++)
                        WriteAgent(w, visible[i]);
                }), reliable: false);
            }
        }

        private static void WriteAgent(BinaryWriter w, Tracked t)
        {
            Agent agent = t.agent;
            w.Write(t.id);
            w.Write(agent.transform.position);
            Vector3 forward = agent.transform.forward;
            w.Write((short)Mathf.RoundToInt(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg * 100f));
            w.Write((byte)Mathf.RoundToInt(Mathf.Clamp01(agent.healthFraction) * 255f));
            int anim = 0;
            float animTime = 0f;
            if (agent.animator && agent.animator.isActiveAndEnabled)
            {
                AnimatorStateInfo state = agent.animator.GetCurrentAnimatorStateInfo(0);
                anim = state.fullPathHash;
                animTime = state.normalizedTime % 1f;
            }
            w.Write(anim);
            w.Write((byte)Mathf.RoundToInt(animTime * 255f));
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
}
