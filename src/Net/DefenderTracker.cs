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
            public readonly UnitVisualStream.Sender visuals = new UnitVisualStream.Sender();
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
        // Each English squad's destination tile as last sent, so watchers' ordering rules match the leader's.
        private static readonly List<NavSpot> sentTiles = new List<NavSpot>();
        private static readonly List<NavSpot> currentTiles = new List<NavSpot>();
        private static readonly List<EnglishSquad> tileSquads = new List<EnglishSquad>();
        private static readonly List<EnglishSquad> sentTileSquads = new List<EnglishSquad>();
        // Which squads could flee, as last sent; checked a few times a second.
        private static string sentEvac = "";
        private static float nextEvac;
        // Each squad's abilities (cooldown, uses left, blocked) as last sent, and when everything was last re-sent.
        private static string sentAbilities = "";
        private static float nextAbilities, nextAbilityRefresh;
        // Who's in which house, as last sent.
        private static string sentOccupants = "";
        private static float nextOccupants, nextOccupantRefresh;

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
            nextSnapshot = 0f;
            UnitVisualStream.Reset();
            sentTiles.Clear();
            sentTileSquads.Clear();
            sentEvac = "";
            nextEvac = 0f;
            sentAbilities = "";
            nextAbilities = nextAbilityRefresh = 0f;
            sentOccupants = "";
            nextOccupants = nextOccupantRefresh = 0f;
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
                // Visuals include inactive indoor/injured units and dying/ragdoll units until destruction.
                t.visuals.Capture(t.id, t.agent, time);
                // Includes Vikings riding in on their ship before they spawn onto the beach.
                if (t.agent.gameObject.activeInHierarchy)
                    visibleAgents.Add(t);
            }
            UnitVisualStream.FlushFrames();
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
            SendSquadTiles();
            SendSquadEvac();
            SendSquadAbilities();
            SendHouseOccupants();
        }

        /// <summary>Whose squad is in (or heading into) each house, for everyone's house labels and teammates' healing orders.</summary>
        private static void SendHouseOccupants()
        {
            if (Time.unscaledTime < nextOccupants || Profile.campaign == null)
                return;
            nextOccupants = Time.unscaledTime + 0.25f;
            Dictionary<int, HouseOccupants.Entry> occupants = HouseOccupants.Read(GameAccess.CurrentIsland);
            string sent = HouseOccupants.Key(occupants);
            bool refresh = Time.unscaledTime >= nextOccupantRefresh;
            if (sent == sentOccupants && !refresh)
                return;
            sentOccupants = sent;
            nextOccupantRefresh = Time.unscaledTime + 2f;
            // Unreliable like the abilities: the refresh repairs a lost one, and it isn't kept for late joiners.
            BattleStream.Send(Packet.Build(MsgType.HouseOccupants, w => HouseOccupants.Write(w, occupants)), reliable: false);
        }

        /// <summary>
        /// Each squad's abilities as the leader has them, for the owners' buttons (SquadAbilityMirror): their copies never
        /// run an ability themselves, so without this they never showed a cooldown, used a charge or saw the squad busy
        /// healing, and every click in that time was refused. Sent when something changes (checked ten times a second),
        /// and all of it every 2 s.
        /// </summary>
        private static void SendSquadAbilities()
        {
            if (Time.unscaledTime < nextAbilities || Profile.campaign == null)
                return;
            nextAbilities = Time.unscaledTime + 0.1f;
            var squads = new List<KeyValuePair<int, List<SquadAbilityMirror.State>>>();
            var key = new System.Text.StringBuilder();
            foreach (Squad candidate in GameAccess.PlayerSquads)
            {
                var squad = candidate as EnglishSquad;
                if (!squad || squad.hero == null || squad.upgradeManager == null) continue;
                int hero = Profile.campaign.heroes.IndexOf(squad.hero);
                List<SquadAbilityMirror.State> states = SquadAbilityMirror.Capture(squad);
                if (states.Count == 0) continue;
                squads.Add(new KeyValuePair<int, List<SquadAbilityMirror.State>>(hero, states));
                key.Append(hero).Append(':');
                foreach (SquadAbilityMirror.State state in states) key.Append(state.hash).Append(',').Append(state.flags).Append(',').Append(state.charges).Append(';');
            }
            string sent = key.ToString();
            bool refresh = Time.unscaledTime >= nextAbilityRefresh;
            if (sent == sentAbilities && !refresh)
                return;
            sentAbilities = sent;
            nextAbilityRefresh = Time.unscaledTime + 2f;
            // Not kept for late joiners (unreliable): it changes all the time, and the refresh repairs a lost one.
            BattleStream.Send(Packet.Build(MsgType.SquadAbilities, w => SquadAbilityMirror.Write(w, squads)), reliable: false);
        }

        /// <summary>
        /// Which battle squads are the mastermind's commanders (so the mastermind can find and order them), with each
        /// one's trait and flag-bearer so every player sees the flag.
        /// </summary>
        public static void SendCommanders()
        {
            var entries = new List<int[]>();
            foreach (var deployed in Game.Economy.DeployedCommanders)
                if (deployed.Value && squadIds.TryGetValue(deployed.Value, out int squadId))
                {
                    Game.VikingCommander commander = Game.MastermindSession.Commander(deployed.Key);
                    Agent bearer = Game.VikingTraits.FlagBearer(deployed.Value);
                    int bearerId = bearer && agentsByAgent.TryGetValue(bearer, out TrackedAgent tracked) ? tracked.id : -1;
                    entries.Add(new[] { deployed.Key, squadId, commander != null ? commander.trait : 0, bearerId });
                }
            BattleStream.Send(Packet.Build(MsgType.CommanderSquads, w =>
            {
                w.Write((byte)entries.Count);
                foreach (int[] entry in entries) { w.Write(entry[0]); w.Write(entry[1]); w.Write((byte)entry[2]); w.Write(entry[3]); }
            }), reliable: true);
        }

        /// <summary>The id watchers know a ship by (for following a commander's boat), or -1.</summary>
        public static int ShipId(Longship ship) => ship && shipIds.TryGetValue(ship, out int id) ? id : -1;

        private static void SendSquadEvac()
        {
            if (Time.unscaledTime < nextEvac || Profile.campaign == null)
                return;
            nextEvac = Time.unscaledTime + 0.25f;
            var heroes = new List<int>();
            var flee = new List<bool>();
            var key = new System.Text.StringBuilder();
            foreach (Squad candidate in GameAccess.PlayerSquads)
            {
                var squad = candidate as EnglishSquad;
                if (!squad || squad.hero == null) continue;
                int hero = Profile.campaign.heroes.IndexOf(squad.hero);
                bool can = Game.Evacuation.CanFlee(squad);
                heroes.Add(hero); flee.Add(can);
                key.Append(hero).Append(can ? '+' : '-');
            }
            string state = key.ToString();
            if (state == sentEvac)
                return;
            sentEvac = state;
            BattleStream.Send(Packet.Build(MsgType.SquadEvac, w =>
            {
                w.Write((byte)heroes.Count);
                for (int i = 0; i < heroes.Count; i++) { w.Write(heroes[i]); w.Write(flee[i]); }
            }), reliable: true);
        }

        private static void SendSquadTiles()
        {
            Island island = GameAccess.CurrentIsland;
            if (!island || Profile.campaign == null)
                return;
            currentTiles.Clear();
            tileSquads.Clear();
            foreach (Squad candidate in GameAccess.PlayerSquads)
            {
                var squad = candidate as EnglishSquad;
                if (!squad || squad.hero == null || !squad.navSpotOccupant || !squad.navSpotOccupant.navSpot)
                    continue;
                tileSquads.Add(squad);
                currentTiles.Add(squad.navSpotOccupant.navSpot);
            }
            bool changed = currentTiles.Count != sentTiles.Count;
            for (int i = 0; !changed && i < currentTiles.Count; i++)
                changed = currentTiles[i] != sentTiles[i] || tileSquads[i] != sentTileSquads[i];
            if (!changed)
                return;
            sentTiles.Clear(); sentTiles.AddRange(currentTiles);
            sentTileSquads.Clear(); sentTileSquads.AddRange(tileSquads);
            var heroes = new int[tileSquads.Count];
            var tiles = new short[tileSquads.Count];
            for (int i = 0; i < heroes.Length; i++)
            {
                heroes[i] = Profile.campaign.heroes.IndexOf(tileSquads[i].hero);
                tiles[i] = (short)island.navSpotter.navSpots.IndexOf(currentTiles[i]);
            }
            BattleStream.Send(Packet.Build(MsgType.SquadTiles, w =>
            {
                w.Write((byte)heroes.Length);
                for (int i = 0; i < heroes.Length; i++) { w.Write(heroes[i]); w.Write(tiles[i]); }
            }), reliable: true);
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
            byte body = hasAnimator ? BodyParameters(agent.animator) : (byte)0;
            bool hasMoving = (body & HasMovingParam) != 0, hasSpeed = (body & HasSpeedParam) != 0;
            w.Write((byte)(hasMoving && agent.animator.GetBool(MovingParam) ? 1 : 0));
            w.Write((ushort)Mathf.Clamp(Mathf.RoundToInt((hasSpeed ? agent.animator.GetFloat(SpeedParam) : 0f) * 100f), 0, 65535));
        }

        private const byte HasMovingParam = 1, HasSpeedParam = 2;

        /// <summary>
        /// Which of Body's parameters a controller has. Animator.parameters allocates a new array on every
        /// call, so each controller is inspected once instead of per unit per snapshot.
        /// </summary>
        private static readonly Dictionary<RuntimeAnimatorController, byte> bodyParameters = new Dictionary<RuntimeAnimatorController, byte>();

        private static byte BodyParameters(Animator animator)
        {
            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            // An animator that hasn't initialised yet can report no parameters; don't cache that.
            if (!controller || !animator.isInitialized)
                return 0;
            if (bodyParameters.TryGetValue(controller, out byte flags))
                return flags;
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == MovingParam && parameter.type == AnimatorControllerParameterType.Bool) flags |= HasMovingParam;
                if (parameter.nameHash == SpeedParam && parameter.type == AnimatorControllerParameterType.Float) flags |= HasSpeedParam;
            }
            bodyParameters[controller] = flags;
            return flags;
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
