using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BadNorthMP.Net;
using BadNorthMP.Spectator;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense;
using Voxels.TowerDefense.Flag;
using Voxels.TowerDefense.RaidGeneration;
using Voxels.TowerDefense.SpriteMagic;
using Voxels.TowerDefense.TriFlow;
using Voxels.TowerDefense.Upgrades;

namespace BadNorthMP.Game
{
    // Commanders sail in: ported from GMW's "Commanders Sail In" 2.4.1 (used with permission).
    // On the campaign leader a deployed commander's squad arrives on a friendly longship at the beach nearest its spot;
    // a village commander walks out of a house once the boats are in. Ships and soldiers reach other players through
    // battle mirroring; the cinematic (camera, black bars, war cry) plays for everyone, driven by MsgType.SailIn.

    internal static class SailIn
    {
        public const byte Follow = 0, ZoomOut = 1;
        public static bool IsSpawningCommanderShip;
        public static readonly HashSet<Longship> FriendlyShips = new HashSet<Longship>();

        public static bool Enabled => ModConfig.SailIn != null && ModConfig.SailIn.Value && !SpectatorSession.Active;

        /// <summary>Leader: a commander's boat is still on its way (Viking waves wait for it).</summary>
        public static bool Arriving
        {
            get
            {
                foreach (CommanderSailInController controller in CommanderSailInController.Active)
                    if (controller && !controller.landed) return true;
                return false;
            }
        }

        /// <summary>Leader: tell everyone watching to follow (or stop following) this boat.</summary>
        public static void Broadcast(byte phase, Longship ship, Vector3 point)
        {
            int id = DefenderTracker.ShipId(ship);
            if (!BattleStream.Active || id < 0) return;
            BattleStream.Send(Packet.Build(MsgType.SailIn, w => { w.Write(phase); w.Write(id); w.Write(point); }), reliable: true);
        }

        private static float loadingSeenAt = -100f;

        /// <summary>Leader: every player has the island loaded (LoadGate) and the loading screen has been gone a moment, so nobody misses the boats.</summary>
        public static bool EveryoneIn
        {
            get
            {
                var stack = Singleton<Voxels.TowerDefense.Stack>.instance;
                if (stack && stack.stateLoading.gameObject.activeInHierarchy) loadingSeenAt = Time.unscaledTime;
                return !LoadGate.Holding && Time.unscaledTime - loadingSeenAt >= 1.5f && Time.unscaledTime - LoadGate.ReleasedAt >= 1.5f;
            }
        }

        /// <summary>Watchers: the leader's cinematic cue for a mirrored boat; someone still loading gets it once they're in, if the boat is still at sea.</summary>
        public static void Received(byte phase, int shipId, Vector3 point)
        {
            if (phase == Follow)
            {
                if (!SpectatorSession.IslandReady()) { SailInCamera.Queue(shipId); return; }
                SailInEffects.WarCry();
                SailInCamera.Begin(shipId, () => PuppetWorld.ShipTransform(shipId));
            }
            else if (phase == ZoomOut)
            {
                SailInCamera.Unqueue(shipId);
                SailInCamera.ZoomOut(shipId, point);
            }
        }

        private static List<Landing> FriendlyLandings()
        {
            var landings = new List<Landing>();
            foreach (Longship ship in FriendlyShips)
                if (ship && ship.landing && ship.gameObject.activeInHierarchy) landings.Add(ship.landing);
            return landings;
        }

        /// <summary>A squad still hidden, as SquadSpawner.SpawnEnglishSquad starts one.</summary>
        private static EnglishSquad NewSquad(HeroDefinition heroDef, NavSpot targetNavSpot, Island island)
        {
            EnglishSquad squad = ScriptableObjectSingleton<PrefabManager>.instance.englishSquad.SpawnGetFromPrefab(targetNavSpot.navPos, island.english) as EnglishSquad;
            squad.squadTemplate = ScriptableObjectSingleton<PrefabManager>.instance.militiaTemplate;
            squad.hero = heroDef;
            squad.gameObject.SetActive(false);
            return squad;
        }

        /// <summary>The hero, upgrades and soldiers, step by step as SpawnEnglishSquad does, but leaving the squad hidden.</summary>
        private static IEnumerable<bool> Build(EnglishSquad squad, HeroDefinition heroDef, NavSpot targetNavSpot)
        {
            yield return true;
            squad.SpawnHero(targetNavSpot.navPos);
            yield return true;
            SquadUpgradeManager upgrades = squad.upgradeManager;
            foreach (UpgradeComponent universal in ScriptableObjectSingleton<PrefabManager>.instance.universalUpgrades)
            {
                upgrades.AddUpgrade(universal, 0);
                yield return false;
            }
            foreach (SerializableHeroUpgrade upgrade in heroDef.upgrades)
            {
                upgrade.definition.OnAppliedToSquad(squad, upgrade.level);
                yield return true;
            }
            IEnumerator fill = squad.FillRoutine(targetNavSpot);
            while (fill.MoveNext()) yield return false;
        }

        private static IEnumerable<bool> Finish(EnglishSquad squad, SquadSpawner spawner)
        {
            squad.OnSquadSpawnComplete();
            yield return false;
            Traverse.Create(spawner).Field<Action<EnglishSquad>>("onSquadSpawned").Value?.Invoke(squad);
            yield return false;
        }

        /// <summary>A recruited commander: across the sea on a longship to the beach nearest its spot.</summary>
        public static IEnumerator<bool> SailInRoutine(HeroDefinition heroDef, NavSpot targetNavSpot, SquadSpawner spawner, Island island)
        {
            EnglishSquad squad = NewSquad(heroDef, targetNavSpot, island);
            foreach (bool step in Build(squad, heroDef, targetNavSpot)) yield return step;

            while (!EveryoneIn) yield return true;

            Longship shipPrefab = island.levelNode.possibleShips.LastOrDefault();
            Landing landing = null;
            if (shipPrefab)
            {
                var landingObject = new GameObject("BNMP Commander landing");
                // In the island's run container, like the raid's own landings: cleared with the fight, not at campaign teardown.
                landingObject.transform.SetParent(island.runContainer ? island.runContainer : island.transform);
                landing = landingObject.AddComponent<Landing>();
                landing.Init(island);
                landing.shipPrefab = shipPrefab;
                List<Beaches.Beach.Pos> beaches = island.GetComponentInChildren<Beaches>().GetBeachPositions(1f);
                Vector3 goal = targetNavSpot.navPos.pos;
                beaches.Sort((a, b) => Vector3.Distance(a.navPos.pos, goal).CompareTo(Vector3.Distance(b.navPos.pos, goal)));
                List<Landing> others = FriendlyLandings();
                bool placed = false;
                foreach (Beaches.Beach.Pos beach in beaches)
                    if (landing.TryPlace(beach.navPos, beach.dir, 4f, others)) { placed = true; break; }
                if (!placed)
                {
                    UnityEngine.Object.Destroy(landingObject);
                    landing = null;
                }
            }

            if (landing)
            {
                SlowDown(landing);
                IsSpawningCommanderShip = true;
                Longship ship;
                try { ship = landing.Spawn(); }
                finally { IsSpawningCommanderShip = false; }
                Bounds bounds = ship.navigationMesh.bounds;
                int count = squad.agents.Count;
                for (int i = 0; i < count; i++)
                {
                    Agent agent = squad.agents[i];
                    Vector3 seat = Vector3.zero;
                    seat.z = ExtraMath.RemapValue(i + 1.5f, 0f, count, -bounds.extents.z, bounds.extents.z);
                    seat.x = UnityEngine.Random.Range(-bounds.extents.x * 0.6f, bounds.extents.x * 0.6f);
                    NavPos navPos = ship.navPos;
                    navPos.pos = seat;
                    agent.navPos = navPos;
                    agent.transform.SetParent(ship.transform);
                    agent.transform.localPosition = seat;
                    ship.AddAgent(agent);
                    agent.Spawn();
                    agent.body.footstepSoundsEnabled = false;
                }
                squad.gameObject.SetActive(true);
                landing.Launch();
                var controller = ship.gameObject.AddComponent<CommanderSailInController>();
                controller.ship = ship;
                controller.squad = squad;
                controller.island = island;
                FabricWrapper.PostEvent(Traverse.Create(spawner).Field<FabricEventReference>("squadSpawn").Value);
                yield return true;
            }
            else
            {
                // No beach fits a boat: the native arrival.
                squad.gameObject.SetActive(true);
                foreach (Agent agent in squad.agents)
                {
                    agent.Spawn();
                    agent.velocity = (agent.navPos.pos - targetNavSpot.navPos.pos) * 4f;
                    Singleton<DustParticles>.instance.SpawnParticles(agent.navPos.wPos);
                }
                yield return true;
            }
            foreach (bool step in Finish(squad, spawner)) yield return step;
        }

        /// <summary>The configured boat speed (the original's default is a little slower than a raid ship).</summary>
        private static void SlowDown(Landing landing)
        {
            float speed = Mathf.Max(0.01f, ModConfig.SailInBoatSpeed.Value);
            object travel = landing.shipTravel;
            var fields = Traverse.Create(travel);
            fields.Field("duration").SetValue(fields.Field<float>("duration").Value / speed);
            fields.Field("fleeDuration").SetValue(fields.Field<float>("fleeDuration").Value / speed);
            landing.shipTravel = (Landing.ShipTravel)travel;
        }

        /// <summary>A village commander: waits indoors until the boats are in, then walks out of a house.</summary>
        public static IEnumerator<bool> LocalCommanderRoutine(HeroDefinition heroDef, NavSpot targetNavSpot, SquadSpawner spawner, Island island)
        {
            EnglishSquad squad = NewSquad(heroDef, targetNavSpot, island);
            foreach (bool step in Build(squad, heroDef, targetNavSpot)) yield return step;

            List<House> houses = island.village.houses.Where(h => h && h.intact && !h.destroyed).ToList();
            House house = houses.Count > 0 ? houses[UnityEngine.Random.Range(0, houses.Count)] : null;
            squad.gameObject.SetActive(true);
            SquadReplenishLocation home = house ? house.GetComponent<SquadReplenishLocation>() : null;
            if (home)
            {
                home.AssignSquad(squad, 100f, 100f);
                List<ReplenishComponent> indoors = Traverse.Create(home).Field<List<ReplenishComponent>>("agents").Value;
                foreach (Agent agent in squad.agents)
                {
                    if (!agent) continue;
                    agent.Spawn();
                    agent.navPos = home.entranceNavPos;
                    agent.transform.position = home.entranceTransform.position;
                    ReplenishComponent replenish = agent.GetOrAddComponent<ReplenishComponent>();
                    Traverse.Create(replenish).Field("location").SetValue(home);
                    // Plant the flag before going in, as the game does: indoors hides the flag pole too, and a hidden
                    // flag can't be planted ("Coroutine couldn't be started because the game object 'Billboard' is inactive").
                    if (agent == squad.heroAgent)
                    {
                        FlagPole pole = Traverse.Create(replenish).Field<FlagPole>("flagPole").Value;
                        if (pole && pole.gameObject.activeInHierarchy) pole.Plant(home.flagTransform);
                    }
                    replenish.indoors.SetActive(true);
                    if (agent == squad.heroAgent) indoors.Insert(0, replenish);
                    else indoors.Add(replenish);
                }
            }
            else
            {
                foreach (Agent agent in squad.agents)
                {
                    if (!agent) continue;
                    agent.transform.position = new Vector3(0f, -100f, 0f);
                    agent.gameObject.SetActive(false);
                }
            }
            for (int i = 0; i < 15; i++) yield return false;
            while (!EveryoneIn) yield return true;
            while (Arriving) yield return false;
            while (SailInCamera.Busy) yield return false;
            for (float wait = 0.5f; wait > 0f; wait -= Time.deltaTime) yield return false;
            if (home) Traverse.Create(home).Method("StartExitHouse", 0f).GetValue();
            else
            {
                foreach (Agent agent in squad.agents)
                {
                    if (!agent) continue;
                    agent.gameObject.SetActive(true);
                    agent.navPos = new NavPos(island.navMesh, targetNavSpot.navPos.pos);
                    agent.transform.position = targetNavSpot.navPos.pos;
                    agent.Spawn();
                    agent.velocity = (agent.navPos.pos - targetNavSpot.navPos.pos) * 4f;
                    Singleton<DustParticles>.instance.SpawnParticles(agent.navPos.wPos);
                    for (float wait = 0.15f; wait > 0f; wait -= Time.deltaTime) yield return false;
                }
            }
            foreach (bool step in Finish(squad, spawner)) yield return step;
        }
    }

    /// <summary>Leader: one commander's boat, from launch until it has dropped its squad and sailed off.</summary>
    internal sealed class CommanderSailInController : MonoBehaviour
    {
        public static readonly List<CommanderSailInController> Active = new List<CommanderSailInController>();
        public Longship ship;
        public EnglishSquad squad;
        public Island island;
        public bool landed;
        private bool following, departed;

        private void Awake() => Active.Add(this);

        private void OnDestroy()
        {
            Active.Remove(this);
            SailInCamera.Abort(Key);
        }

        private int Key => DefenderTracker.ShipId(ship) >= 0 ? DefenderTracker.ShipId(ship) : GetInstanceID();

        private void Start()
        {
            SailInEffects.WarCry();
            following = SailInCamera.Begin(Key, () => ship ? ship.transform : null);
            SailIn.Broadcast(SailIn.Follow, ship, Vector3.zero);
        }

        private void Update()
        {
            if (!ship || !squad)
            {
                Destroy(this);
                return;
            }
            if (following && ship.interpolator > 0.55f)
            {
                following = false;
                Vector3 shore = ship.landing ? ship.landing.navPos.pos : ship.transform.position;
                SailInCamera.ZoomOut(Key, shore);
                SailIn.Broadcast(SailIn.ZoomOut, ship, shore);
            }
            if (!landed && ship.landed)
            {
                landed = true;
                if (ship.col) ship.col.enabled = false;
                foreach (Agent agent in squad.agents)
                {
                    if (!agent) continue;
                    agent.transform.SetParent(squad.transform);
                    agent.navPos = new NavPos(island.navMesh, agent.transform.position);
                    agent.body.footstepSoundsEnabled = true;
                }
                StartCoroutine(Depart());
            }
            if (landed && departed) Destroy(this);
        }

        private IEnumerator Depart()
        {
            yield return new WaitForSeconds(2.5f);
            if (ship && ship.gameObject.activeInHierarchy) ship.Launch();
            departed = true;
        }
    }

    /// <summary>
    /// The cinematic's camera, for the leader's real boat and watchers' mirrored ones: zoom in and follow, then glide
    /// back out over the landing beach.
    /// </summary>
    internal sealed class SailInCamera : MonoBehaviour
    {
        private static SailInCamera instance;
        private LevelCamera cam;
        private Transform rootPan, panX, panY;
        private Vector3 originalPanX, originalPanY, velocity;
        private MonoBehaviour zoomer, dragger;
        private Func<Transform> target;
        private int owner;
        private bool active, following;
        private float began;
        // A boat to follow once this player's island has finished loading (-1: none).
        private static int queued = -1;

        public static bool Busy => instance && instance.active;

        public static void Queue(int key)
        {
            Instance();
            queued = key;
        }

        public static void Unqueue(int key)
        {
            if (queued == key) queued = -1;
        }

        private static SailInCamera Instance()
        {
            if (instance) return instance;
            var host = new GameObject("BNMP Sail-in cinematic");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<SailInCamera>();
            host.AddComponent<SailInEffects>();
            return instance;
        }

        /// <summary>Start following; false if the option is off, there's no level camera, or another boat has it.</summary>
        public static bool Begin(int key, Func<Transform> follow)
        {
            if (!ModConfig.SailInCamera.Value || Busy) return false;
            SailInCamera self = Instance();
            LevelCamera camera = Singleton<LevelCamera>.instance;
            if (!camera) return false;
            var fields = Traverse.Create(camera);
            self.cam = camera;
            self.rootPan = fields.Field<Transform>("rootPanTransform").Value;
            self.panY = fields.Field<Transform>("panYTransform").Value;
            self.panX = fields.Field<Transform>("panXTransform").Value;
            if (!self.rootPan) return false;
            self.originalPanY = self.panY ? self.panY.localPosition : Vector3.zero;
            self.originalPanX = self.panX ? self.panX.localPosition : Vector3.zero;
            self.zoomer = camera.GetComponent("CameraZoomer") as MonoBehaviour;
            self.dragger = camera.GetComponent("CameraDragger") as MonoBehaviour;
            camera.enabled = false;
            if (self.zoomer) self.zoomer.enabled = false;
            if (self.dragger) self.dragger.enabled = false;
            self.owner = key;
            self.target = follow;
            self.velocity = Vector3.zero;
            self.active = self.following = true;
            self.began = Time.unscaledTime;
            SailInEffects.Bars(true);
            return true;
        }

        public static void ZoomOut(int key, Vector3 shore)
        {
            if (!Busy || instance.owner != key || !instance.following) return;
            instance.following = false;
            SailInEffects.Bars(false);
            instance.StartCoroutine(instance.ZoomOutAndRestore(shore));
        }

        public static void Abort(int key)
        {
            if (Busy && instance.owner == key) instance.Restore();
        }

        private void Update()
        {
            if (queued >= 0 && !active && SpectatorSession.IslandReady())
            {
                int key = queued;
                queued = -1;
                SailInEffects.WarCry();
                Begin(key, () => PuppetWorld.ShipTransform(key));
            }
            if (!active) return;
            if (!cam || !rootPan) { Restore(); return; }
            if (!following) return;
            Transform boat = target != null ? target() : null;
            if (!boat)
            {
                // A mirrored boat shows up with its first position update; give it a moment.
                if (Time.unscaledTime - began > 3f) Restore();
                return;
            }
            rootPan.position = Vector3.SmoothDamp(rootPan.position, boat.position, ref velocity, 0.15f);
            cam.orthoHeight = Mathf.Lerp(cam.orthoHeight, 4f, Time.deltaTime * 3f);
            if (panX) panX.localPosition = Vector3.Lerp(panX.localPosition, new Vector3(0f, panX.localPosition.y, panX.localPosition.z), Time.deltaTime * 3f);
            if (panY) panY.localPosition = Vector3.Lerp(panY.localPosition, new Vector3(panY.localPosition.x, 0f, panY.localPosition.z), Time.deltaTime * 3f);
        }

        private IEnumerator ZoomOutAndRestore(Vector3 shore)
        {
            Vector3 start = rootPan.position;
            float startHeight = cam.orthoHeight;
            for (float t = 0f; t < 1f && active && cam && rootPan;)
            {
                t += Time.deltaTime * 0.75f;
                float eased = Mathf.SmoothStep(0f, 1f, t);
                rootPan.position = Vector3.Lerp(start, shore, eased);
                cam.orthoHeight = Mathf.Lerp(startHeight, 16f, eased);
                if (panX) panX.localPosition = Vector3.Lerp(panX.localPosition, originalPanX, eased);
                if (panY) panY.localPosition = Vector3.Lerp(panY.localPosition, originalPanY, eased);
                yield return null;
            }
            Restore();
        }

        private void Restore()
        {
            if (!active) return;
            active = following = false;
            StopAllCoroutines();
            SailInEffects.Bars(false);
            if (cam)
            {
                if (rootPan) Traverse.Create(cam).Field("currentSquadSelectOffset").SetValue(rootPan.position);
                cam.enabled = true;
            }
            if (zoomer) zoomer.enabled = true;
            if (dragger) dragger.enabled = true;
        }
    }

    /// <summary>The cinematic's black bars and war cry.</summary>
    internal sealed class SailInEffects : MonoBehaviour
    {
        private static SailInEffects instance;
        private RectTransform top, bottom;
        private bool barsWanted;
        private AudioSource source;
        private readonly List<AudioClip> cries = new List<AudioClip>();
        private float lastCry = -100f;

        private void Awake()
        {
            instance = this;
            var canvasObject = new GameObject("Bars");
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            top = Bar(canvasObject.transform, "Top", 1f);
            bottom = Bar(canvasObject.transform, "Bottom", 0f);
            source = gameObject.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            StartCoroutine(Load("audio1.ogg"));
            StartCoroutine(Load("audio2.ogg"));
        }

        private static RectTransform Bar(Transform parent, string name, float edge)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, edge);
            rect.anchorMax = new Vector2(1f, edge);
            rect.pivot = new Vector2(0.5f, edge);
            rect.sizeDelta = Vector2.zero;
            return rect;
        }

        private IEnumerator Load(string name)
        {
            string path = EmbeddedAssets.File(name);
            if (path == null) yield break;
#pragma warning disable 618 // WWW is how this Unity version reads an ogg from disk
            using (var www = new WWW("file:///" + path.Replace("\\", "/")))
            {
                yield return www;
                if (string.IsNullOrEmpty(www.error)) cries.Add(www.GetAudioClip(false, false, AudioType.OGGVORBIS));
                else ModLog.Warn("Couldn't load the sail-in war cry " + name + ": " + www.error);
            }
#pragma warning restore 618
        }

        private static SailInEffects Instance()
        {
            if (!instance)
            {
                var host = new GameObject("BNMP Sail-in effects");
                DontDestroyOnLoad(host);
                host.AddComponent<SailInEffects>();
            }
            return instance;
        }

        public static void Bars(bool shown) => Instance().barsWanted = shown;

        public static void WarCry()
        {
            SailInEffects self = Instance();
            if (!ModConfig.SailInWarCry.Value || self.cries.Count == 0 || Time.time - self.lastCry < 5f) return;
            self.lastCry = Time.time;
            self.source.PlayOneShot(self.cries[UnityEngine.Random.Range(0, self.cries.Count)]);
        }

        private void Update()
        {
            float goal = barsWanted && ModConfig.SailInBars.Value ? Screen.height * 0.12f : 0f;
            float height = Mathf.Lerp(top.sizeDelta.y, goal, Time.unscaledDeltaTime * 3.5f);
            top.sizeDelta = bottom.sizeDelta = new Vector2(0f, height);
        }
    }

    [HarmonyPatch(typeof(SquadSpawner), nameof(SquadSpawner.SpawnEnglishSquad))]
    internal static class SailInDeployPatch
    {
        private static bool Prefix(HeroDefinition heroDef, NavSpot navSpot, SquadSpawner __instance, ref IEnumerator<bool> __result)
        {
            if (!SailIn.Enabled) return true;
            IslandGameplayManager manager = Traverse.Create(__instance).Field<IslandGameplayManager>("manager").Value;
            Island island = manager ? manager.island : null;
            if (!island || island.levelNode.isStart) return true;
            __result = heroDef.recruited
                ? SailIn.SailInRoutine(heroDef, navSpot, __instance, island)
                : SailIn.LocalCommanderRoutine(heroDef, navSpot, __instance, island);
            return false;
        }
    }

    /// <summary>A commander's boat has no raid ship group to time its departure by.</summary>
    [HarmonyPatch(typeof(Landing), nameof(Landing.startTime), MethodType.Getter)]
    internal static class SailInLandingTimePatch
    {
        private static bool Prefix(Landing __instance, ref float __result)
        {
            if (__instance.shipGroup) return true;
            __result = 0f;
            return false;
        }
    }

    /// <summary>A commander's boat: spawned facing the island, never counted as a threat, never exploding into debris.</summary>
    [HarmonyPatch(typeof(Landing), nameof(Landing.Spawn))]
    internal static class SailInShipSpawnPatch
    {
        private static bool Prefix(Landing __instance, ref Longship __result)
        {
            if (!SailIn.IsSpawningCommanderShip) return true;
            __instance.gameObject.SetActive(false);
            Longship ship = UnityEngine.Object.Instantiate(__instance.shipPrefab.gameObject, __instance.pos,
                Quaternion.LookRotation(__instance.dir), __instance.transform).GetComponent<Longship>();
            ship.transform.Rotate(0f, 180f, 0f);
            ship.Setup(__instance);
            var manager = Singleton<IslandGameplayManager>.instance;
            if (ship.navigationMesh && manager && manager.island) ship.navigationMesh.island = manager.island;
            SailIn.FriendlyShips.Add(ship);
            CorpseManager corpses = ship.GetComponentInChildren<CorpseManager>();
            if (corpses)
            {
                corpses.corpseCount = 10;
                corpses.Precache();
            }
            Traverse.Create(__instance).Property("spawnedShip").SetValue(ship);
            ExtraRenderers.InitializeAll(ship.transform);
            foreach (BatchedSprite sprite in __instance.GetComponentsInChildren<BatchedSprite>(true)) sprite.Awake();
            __result = ship;
            return false;
        }
    }

    [HarmonyPatch(typeof(Longship), nameof(Longship.Explode))]
    internal static class SailInShipExplodePatch
    {
        private static bool Prefix(Longship __instance)
        {
            if (!SailIn.FriendlyShips.Contains(__instance)) return true;
            Singleton<CameraShaker>.instance.ShakeOnce(__instance.landForce * 0.2f);
            return false;
        }
    }

    [HarmonyPatch(typeof(Longship), nameof(Longship.data), MethodType.Getter)]
    internal static class SailInShipThreatPatch
    {
        private static bool Prefix(Longship __instance, ref Data __result)
        {
            if (!SailIn.FriendlyShips.Contains(__instance)) return true;
            __result = default(Data);
            return false;
        }
    }

    [HarmonyPatch(typeof(Longship), "OnDestroy")]
    internal static class SailInShipCleanupPatch
    {
        private static void Prefix(Longship __instance) => SailIn.FriendlyShips.Remove(__instance);
    }

    /// <summary>
    /// Native Viking waves wait until the commanders' boats are in. Mastermind waves are launched by CustomWaves, which
    /// needs the first step at once, so a due one waits in Economy.Tick instead.
    /// </summary>
    [HarmonyPatch(typeof(Wave), nameof(Wave.BeginWave))]
    internal static class SailInWaveDelayPatch
    {
        private static void Postfix(ref IEnumerator __result)
        {
            if (Economy.Active || !SailIn.Arriving) return;
            __result = Delayed(__result);
        }

        private static IEnumerator Delayed(IEnumerator original)
        {
            while (SailIn.Arriving) yield return null;
            while (original.MoveNext()) yield return original.Current;
        }
    }
}
