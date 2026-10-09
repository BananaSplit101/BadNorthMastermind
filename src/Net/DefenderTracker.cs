using System.Collections.Generic;
using System.IO;
using EventAction = Fabric.EventAction;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.Ballistics;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Defender side: gives every squad, agent, longship and projectile a network id as it appears, and streams
    /// what the mastermind needs to mirror them: creation, spawn, death, removal, 20 Hz transform/animation
    /// snapshots, house fire states, and the sounds and effects the battle plays.
    /// State changes are found by polling at the snapshot rate.
    /// </summary>
    internal static class DefenderTracker
    {
        private const float SnapshotInterval = 0.05f;
        private const int MaxPacketBytes = 1100; // Steam unreliable P2P packets max out around 1200 bytes
        private const int AgentRecordBytes = 4 + 12 + 8 + 1 + 4 + 2 + 1 + 2;
        private const float HouseFireStep = 0.03f;
        private static readonly int MovingParam = Animator.StringToHash("Moving");
        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private const int ShipRecordBytes = 4 + 12 + 16 + 4 + 2;
        private const int ProjectileRecordBytes = 4 + 12 + 8;

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

        private class TrackedProjectile
        {
            public int id;
            public GameObject go;
        }

        private static readonly Dictionary<Squad, int> squadIds = new Dictionary<Squad, int>();
        private static readonly Dictionary<Agent, TrackedAgent> agentsByAgent = new Dictionary<Agent, TrackedAgent>();
        private static readonly List<TrackedAgent> agents = new List<TrackedAgent>();
        private static readonly Dictionary<Longship, int> shipIds = new Dictionary<Longship, int>();
        private static readonly List<TrackedShip> ships = new List<TrackedShip>();
        private static readonly Dictionary<GameObject, TrackedProjectile> projectilesByObject = new Dictionary<GameObject, TrackedProjectile>();
        private static readonly List<TrackedProjectile> projectiles = new List<TrackedProjectile>();
        private static byte[] houseFlags = new byte[0];
        private static float[] houseFire = new float[0];
        private static int nextId = 1;
        private static float nextSnapshot;

        public static void Reset()
        {
            squadIds.Clear();
            agentsByAgent.Clear();
            agents.Clear();
            shipIds.Clear();
            ships.Clear();
            projectilesByObject.Clear();
            projectiles.Clear();
            houseFlags = new byte[0];
            houseFire = new float[0];
            nextId = 1;
        }

        // ---- Things appearing ----------------------------------------------------------------

        public static void OnAgentCreated(Squad squad, Agent prefab, Agent agent)
        {
            if (!BattleStream.Active || !agent || agentsByAgent.ContainsKey(agent))
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
            agentsByAgent[agent] = entry;
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
            if (!BattleStream.Active || !ship || !landing.shipPrefab || shipIds.ContainsKey(ship))
                return;
            var entry = new TrackedShip { id = nextId++, ship = ship, animator = ship.GetComponent<Animator>() };
            ships.Add(entry);
            shipIds[ship] = entry.id;
            string prefabName = landing.shipPrefab.name;
            BattleStream.Send(Packet.Build(MsgType.ShipSpawn, w =>
            {
                w.Write(entry.id);
                w.Write(prefabName);
            }), reliable: true);
        }

        /// <summary>Arrows, thrown axes, torches and bombs. They are pooled, so "gone" means deactivated.</summary>
        public static void OnProjectileLaunched(Component projectile)
        {
            if (!BattleStream.Active || !projectile)
                return;
            GameObject go = projectile.gameObject;
            if (projectilesByObject.TryGetValue(go, out TrackedProjectile existing) && existing.go)
                return; // still in flight from an earlier launch
            var entry = new TrackedProjectile { id = nextId++, go = go };
            projectiles.Add(entry);
            projectilesByObject[go] = entry;
            string prefabName = go.name.Replace("(Clone)", "").Trim();
            BattleStream.Send(Packet.Build(MsgType.ProjectileSpawn, w =>
            {
                w.Write(entry.id);
                w.Write(prefabName);
            }), reliable: true);
        }

        // ---- Sounds and effects --------------------------------------------------------------

        public static void OnSound(string name, int id, int action, GameObject parent)
        {
            if (!BattleStream.Active)
                return;
            if (name != null && name.StartsWith("UI/"))
                return; // the defender's own menu clicks
            if (name == null && !parent)
                return; // anonymous global event by id: most likely UI

            byte target = SoundTarget.Global;
            int targetId = 0;
            Vector3 pos = Vector3.zero;
            if (parent)
            {
                pos = parent.transform.position;
                target = SoundTarget.Position;
                Agent agent = parent.GetComponentInParent<Agent>();
                Longship ship = agent ? null : parent.GetComponentInParent<Longship>();
                House house = agent || ship ? null : parent.GetComponentInParent<House>();
                if (agent && agentsByAgent.TryGetValue(agent, out TrackedAgent tracked))
                {
                    target = SoundTarget.Agent;
                    targetId = tracked.id;
                }
                else if (ship && shipIds.TryGetValue(ship, out int shipId))
                {
                    target = SoundTarget.Ship;
                    targetId = shipId;
                }
                else if (house)
                {
                    House[] houses = GameAccess.CurrentIsland ? GameAccess.CurrentIsland.village.houses : null;
                    int index = houses != null ? System.Array.IndexOf(houses, house) : -1;
                    if (index >= 0)
                    {
                        target = SoundTarget.House;
                        targetId = index;
                    }
                }
            }

            BattleStream.Send(Packet.Build(MsgType.Sound, w =>
            {
                w.Write(target);
                w.Write(targetId);
                w.Write(pos);
                w.Write(name != null);
                if (name != null)
                    w.Write(name);
                else
                    w.Write(id);
                w.Write(action);
            }), reliable: false);
        }

        public static void OnDust(Vector3 pos, Vector3 dir)
        {
            if (BattleStream.Active)
                BattleStream.Send(Packet.Build(MsgType.Dust, w =>
                {
                    w.Write(pos);
                    w.Write(dir);
                }), reliable: false);
        }

        public static void OnEffect(ReusableEffect effect, Vector3 pos, Quaternion rotation, Vector3 scale, bool onNavMesh)
        {
            if (!BattleStream.Active || !effect)
                return;
            string name = effect.name.Replace("(Clone)", "").Trim();
            BattleStream.Send(Packet.Build(MsgType.Effect, w =>
            {
                w.Write(name);
                w.Write(pos);
                w.WriteRotation(rotation);
                w.Write(scale);
                w.Write(onNavMesh);
            }), reliable: false);
        }

        // ---- Per-frame -----------------------------------------------------------------------

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
                    if (t.agent.faction && t.agent.faction.side == Faction.Side.English)
                        Game.Economy.OnDefenderKilled(hero: t.agent.GetComponent<Standard>());
                    BattleStream.Send(Packet.Build(MsgType.AgentDied, w => w.Write(t.id)), reliable: true);
                }
                // Includes Vikings riding in on their ship before they spawn onto the beach.
                if (t.agent.gameObject.activeInHierarchy && !t.died)
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

            var flying = new List<TrackedProjectile>();
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                TrackedProjectile p = projectiles[i];
                if (!p.go || !p.go.activeInHierarchy)
                {
                    BattleStream.Send(Packet.Build(MsgType.ProjectileGone, w => w.Write(p.id)), reliable: true);
                    projectiles.RemoveAt(i);
                    if (p.go)
                        projectilesByObject.Remove(p.go);
                    continue;
                }
                flying.Add(p);
            }
            SendInPackets(MsgType.ProjectileSnapshot, time, flying, ProjectileRecordBytes, WriteProjectile);

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
            w.WriteRotation(agent.transform.rotation); // full rotation: knocked-down units tumble
            w.Write((byte)Mathf.RoundToInt(Mathf.Clamp01(agent.healthFraction) * 255f));
            WriteAnimation(w, agent.animator);
            // Body drives these; the animator's transitions depend on them (otherwise Walk falls back to Idle).
            bool hasAnimator = agent.animator && agent.animator.isActiveAndEnabled;
            w.Write((byte)(hasAnimator && agent.animator.GetBool(MovingParam) ? 1 : 0));
            w.Write((ushort)Mathf.Clamp(Mathf.RoundToInt((hasAnimator ? agent.animator.GetFloat(SpeedParam) : 0f) * 100f), 0, 65535));
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

        private static void WriteProjectile(BinaryWriter w, TrackedProjectile p)
        {
            w.Write(p.id);
            w.Write(p.go.transform.position);
            w.WriteRotation(p.go.transform.rotation);
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
                // A finished one-shot (Throw, Attack) holds its last frame; Animator.Play at exactly 1.0 can
                // show the first frame instead (e.g. the torch back in hand), so stop just short of the end.
                time = state.loop ? state.normalizedTime % 1f : Mathf.Clamp(state.normalizedTime, 0f, 0.999f);
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
            {
                houseFlags = new byte[houses.Length];
                houseFire = new float[houses.Length];
            }
            for (int i = 0; i < houses.Length && i < 256; i++)
            {
                House house = houses[i];
                if (!house)
                    continue;
                byte flags = 0;
                if (house.burning != null && house.burning.active) flags |= HouseFlags.Burning;
                if (house.destroying != null && house.destroying.active) flags |= HouseFlags.Destroying;
                if (house.pillaged != null && house.pillaged.active) flags |= HouseFlags.Pillaged;
                float fire = house.fire;
                bool fireChanged = Mathf.Abs(fire - houseFire[i]) >= HouseFireStep || (fire == 0f && houseFire[i] != 0f);
                if (flags == houseFlags[i] && !fireChanged)
                    continue;
                houseFlags[i] = flags;
                houseFire[i] = fire;
                byte index = (byte)i;
                BattleStream.Send(Packet.Build(MsgType.HouseState, w =>
                {
                    w.Write(index);
                    w.Write(flags);
                    w.Write((ushort)Mathf.Clamp(Mathf.RoundToInt(fire * 1000f), 0, 65535));
                }), reliable: true);
            }
        }
    }

    // ---- Defender-side hooks (all inactive while spectating) ------------------------------------

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

    [HarmonyPatch(typeof(Shootable), nameof(Shootable.Shoot))]
    internal static class DefenderShootPatch
    {
        private static void Postfix(Shootable __instance)
        {
            if (!Spectator.SpectatorSession.Active)
                DefenderTracker.OnProjectileLaunched(__instance);
        }
    }

    [HarmonyPatch(typeof(Torch), nameof(Torch.Shoot))]
    internal static class DefenderTorchPatch
    {
        private static void Postfix(Torch __instance)
        {
            if (!Spectator.SpectatorSession.Active)
                DefenderTracker.OnProjectileLaunched(__instance);
        }
    }

    [HarmonyPatch(typeof(BombProjectile), nameof(BombProjectile.Setup))]
    internal static class DefenderBombPatch
    {
        private static void Postfix(BombProjectile __instance)
        {
            if (!Spectator.SpectatorSession.Active)
                DefenderTracker.OnProjectileLaunched(__instance);
        }
    }

    [HarmonyPatch(typeof(ReusableEffect), nameof(ReusableEffect.PlayAt), typeof(Vector3), typeof(Quaternion), typeof(Vector3))]
    internal static class DefenderEffectPatch
    {
        private static void Prefix(ReusableEffect __instance, Vector3 pos, Quaternion rotation, Vector3 scale)
        {
            if (!Spectator.SpectatorSession.Active)
                DefenderTracker.OnEffect(__instance, pos, rotation, scale, onNavMesh: false);
        }
    }

    [HarmonyPatch(typeof(ReusableEffect), nameof(ReusableEffect.PlayAt), typeof(NavPos), typeof(Quaternion), typeof(Vector3))]
    internal static class DefenderNavEffectPatch
    {
        private static void Prefix(ReusableEffect __instance, NavPos navPos, Quaternion rotation, Vector3 scale)
        {
            if (!Spectator.SpectatorSession.Active && navPos.valid)
                DefenderTracker.OnEffect(__instance, navPos.wPos, rotation, scale, onNavMesh: true);
        }
    }

    [HarmonyPatch(typeof(DustParticles), nameof(DustParticles.SpawnParticles), typeof(Vector3), typeof(Vector3))]
    internal static class DefenderDustPatch
    {
        private static void Prefix(Vector3 pos, Vector3 dir)
        {
            if (!Spectator.SpectatorSession.Active)
                DefenderTracker.OnDust(pos, dir);
        }
    }

    // FabricWrapper funnels every sound; mirror the overloads the battle uses.
    [HarmonyPatch(typeof(FabricWrapper), nameof(FabricWrapper.PostEvent), typeof(string), typeof(GameObject))]
    internal static class DefenderSoundPatch1
    {
        private static void Prefix(string eventName, GameObject parentGameObject)
        {
            if (!Spectator.SpectatorSession.Active)
                DefenderTracker.OnSound(eventName, 0, -1, parentGameObject);
        }
    }

    [HarmonyPatch(typeof(FabricWrapper), nameof(FabricWrapper.PostEvent), typeof(string), typeof(EventAction), typeof(GameObject))]
    internal static class DefenderSoundPatch2
    {
        private static void Prefix(string eventName, EventAction eventAction, GameObject parentGameObject)
        {
            if (!Spectator.SpectatorSession.Active)
                DefenderTracker.OnSound(eventName, 0, (int)eventAction, parentGameObject);
        }
    }

    [HarmonyPatch(typeof(FabricWrapper), nameof(FabricWrapper.PostEvent), typeof(int), typeof(GameObject))]
    internal static class DefenderSoundPatch3
    {
        private static void Prefix(int eventId, GameObject parentGameObject)
        {
            if (!Spectator.SpectatorSession.Active)
                DefenderTracker.OnSound(null, eventId, -1, parentGameObject);
        }
    }

    [HarmonyPatch(typeof(FabricWrapper), nameof(FabricWrapper.PostEvent), typeof(int), typeof(EventAction), typeof(GameObject))]
    internal static class DefenderSoundPatch4
    {
        private static void Prefix(int eventId, EventAction eventAction, GameObject parentGameObject)
        {
            if (!Spectator.SpectatorSession.Active)
                DefenderTracker.OnSound(null, eventId, (int)eventAction, parentGameObject);
        }
    }

    [HarmonyPatch(typeof(FabricWrapper), nameof(FabricWrapper.PostEvent), typeof(string))]
    internal static class DefenderSoundPatch5
    {
        private static void Prefix(string eventName)
        {
            if (!Spectator.SpectatorSession.Active)
                DefenderTracker.OnSound(eventName, 0, -1, null);
        }
    }

    [HarmonyPatch(typeof(FabricWrapper), nameof(FabricWrapper.PostEvent), typeof(string), typeof(EventAction))]
    internal static class DefenderSoundPatch6
    {
        private static void Prefix(string eventName, EventAction eventAction)
        {
            if (!Spectator.SpectatorSession.Active)
                DefenderTracker.OnSound(eventName, 0, (int)eventAction, null);
        }
    }
}
