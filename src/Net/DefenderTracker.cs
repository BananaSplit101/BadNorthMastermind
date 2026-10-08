using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Defender side of the battle stream. The mastermind runs the battle locally too, so this only sends
    /// what keeps it faithful: which units exist (English squads/agents; Viking ships are generated
    /// identically on both sides and just matched up), squad positions, wave launches, deaths, house fires,
    /// and 20 Hz transform/health snapshots used to correct drift.
    /// </summary>
    internal static class DefenderTracker
    {
        private const float SnapshotInterval = 0.05f;
        private const int MaxPacketBytes = 1100; // Steam unreliable P2P packets max out around 1200 bytes
        private const int AgentRecordBytes = 4 + 12 + 2 + 1 + 4 + 2;

        private class TrackedAgent
        {
            public int id;
            public Agent agent;
            public bool spawned, died;
        }

        private class TrackedSquad
        {
            public int id;
            public EnglishSquad squad;
            public int navSpot = -1;
        }

        private static readonly Dictionary<Squad, int> squadIds = new Dictionary<Squad, int>();
        private static readonly List<TrackedSquad> englishSquads = new List<TrackedSquad>();
        private static readonly HashSet<Agent> known = new HashSet<Agent>();
        private static readonly List<TrackedAgent> agents = new List<TrackedAgent>();
        private static byte[] houseFlags = new byte[0];
        private static int nextId = 1;
        private static float nextSnapshot;
        private static bool running;

        /// <summary>While Landing.Spawn runs, the Vikings it creates are collected here instead of announced.</summary>
        private static List<int> landingAgents;

        public static void Reset()
        {
            squadIds.Clear();
            englishSquads.Clear();
            known.Clear();
            agents.Clear();
            houseFlags = new byte[0];
            nextId = 1;
            running = false;
            landingAgents = null;
        }

        public static void OnAgentCreated(Squad squad, Agent prefab, Agent agent)
        {
            if (!BattleStream.Active || !agent || !known.Add(agent))
                return;
            var entry = new TrackedAgent { id = nextId++, agent = agent };
            agents.Add(entry);

            if (landingAgents != null)
            {
                landingAgents.Add(entry.id); // a Viking created by Landing.Spawn: the mastermind makes its own
                return;
            }

            int squadId = AnnounceSquad(squad);
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

        private static int AnnounceSquad(Squad squad)
        {
            if (squadIds.TryGetValue(squad, out int squadId))
                return squadId;
            squadId = squadIds[squad] = nextId++;
            var english = squad as EnglishSquad;
            int heroIndex = english && english.hero != null ? Profile.campaign.heroes.IndexOf(english.hero) : -1;
            byte side = (byte)(squad.faction && squad.faction.side == Faction.Side.English ? 0 : 1);
            Vector3 pos = squad.transform.position;
            if (english)
                englishSquads.Add(new TrackedSquad { id = squadId, squad = english });
            BattleStream.Send(Packet.Build(MsgType.SquadSpawn, w =>
            {
                w.Write(squadId);
                w.Write(side);
                w.Write(heroIndex);
                w.Write(pos);
            }), reliable: true);
            return squadId;
        }

        public static void OnLandingSpawnBegin() => landingAgents = BattleStream.Active ? new List<int>() : null;

        public static void OnLandingSpawnEnd(Landing landing)
        {
            List<int> ids = landingAgents;
            landingAgents = null;
            if (ids == null || !TryGetLandingKey(landing, out int wave, out int group, out int index))
                return;
            BattleStream.Send(Packet.Build(MsgType.LandingSpawned, w =>
            {
                w.Write(wave);
                w.Write(group);
                w.Write(index);
                w.Write((short)ids.Count);
                foreach (int id in ids)
                    w.Write(id);
            }), reliable: true);
        }

        /// <summary>Where a landing sits in its raid. Raid generation is seeded, so this matches on both sides.</summary>
        public static bool TryGetLandingKey(Landing landing, out int wave, out int group, out int index)
        {
            wave = group = index = -1;
            ShipGroup shipGroup = landing.shipGroup;
            if (!shipGroup || !shipGroup.wave || !shipGroup.wave.raid)
                return false;
            wave = shipGroup.wave.raid.waves.IndexOf(shipGroup.wave);
            group = shipGroup.wave.shipGroups.IndexOf(shipGroup);
            index = shipGroup.landings.IndexOf(landing);
            return wave >= 0 && group >= 0 && index >= 0;
        }

        public static void OnWaveLaunched(int waveIndex)
        {
            if (BattleStream.Active)
                BattleStream.Send(Packet.Build(MsgType.WaveLaunch, w => w.Write(waveIndex)), reliable: true);
        }

        public static void Update()
        {
            if (!BattleStream.Active || Time.unscaledTime < nextSnapshot)
                return;
            nextSnapshot = Time.unscaledTime + SnapshotInterval;

            var visible = new List<TrackedAgent>();
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
                if (t.spawned && !t.died)
                    visible.Add(t);
            }
            SendSnapshots(visible);
            SendSquadPositions();
            SendHouseChanges();
            SendPhase();
        }

        private static void SendSnapshots(List<TrackedAgent> visible)
        {
            float time = BattleStream.StreamTime;
            int perPacket = (MaxPacketBytes - 7) / AgentRecordBytes;
            for (int start = 0; start < visible.Count; start += perPacket)
            {
                int count = Mathf.Min(perPacket, visible.Count - start);
                int first = start;
                BattleStream.Send(Packet.Build(MsgType.Snapshot, w =>
                {
                    w.Write(time);
                    w.Write((short)count);
                    for (int i = first; i < first + count; i++)
                        WriteAgent(w, visible[i]);
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
            int anim = 0;
            float time = 0f;
            if (agent.animator && agent.animator.isActiveAndEnabled)
            {
                AnimatorStateInfo state = agent.animator.GetCurrentAnimatorStateInfo(0);
                anim = state.fullPathHash;
                time = state.loop ? state.normalizedTime % 1f : Mathf.Clamp01(state.normalizedTime);
            }
            w.Write(anim);
            w.Write((ushort)Mathf.RoundToInt(time * 65535f));
        }

        /// <summary>Covers every way a squad moves: clicks, abilities, swaps.</summary>
        private static void SendSquadPositions()
        {
            foreach (TrackedSquad s in englishSquads)
            {
                if (!s.squad || !s.squad.navSpotOccupant || !s.squad.navSpotOccupant.navSpot)
                    continue;
                int index = s.squad.navSpotOccupant.navSpot.index;
                if (index == s.navSpot)
                    continue;
                s.navSpot = index;
                int squadId = s.id;
                BattleStream.Send(Packet.Build(MsgType.SquadNavSpot, w =>
                {
                    w.Write(squadId);
                    w.Write(index);
                }), reliable: true);
            }
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

        private static void SendPhase()
        {
            var manager = Singleton<IslandGameplayManager>.instance;
            if (running || !manager || manager.states == null || !manager.states.running.active)
                return;
            running = true;
            BattleStream.Send(Packet.Build(MsgType.BattlePhase, w => w.Write((byte)1)), reliable: true);
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
        private static void Prefix()
        {
            if (!Spectator.SpectatorSession.Active)
                DefenderTracker.OnLandingSpawnBegin();
        }

        private static void Postfix(Landing __instance)
        {
            if (!Spectator.SpectatorSession.Active)
                DefenderTracker.OnLandingSpawnEnd(__instance);
        }
    }

    [HarmonyPatch(typeof(Raid), nameof(Raid.BeginNextWave))]
    internal static class DefenderWaveLaunchPatch
    {
        private static void Prefix(int ___waveIndex)
        {
            if (!Spectator.SpectatorSession.Active)
                DefenderTracker.OnWaveLaunched(___waveIndex);
        }
    }
}
