using System.Collections.Generic;
using BadNorthMP.Spectator;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;
using Voxels;
using Voxels.TowerDefense;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Nobody's battle starts while someone is still loading it. The leader sends the battle the moment its island
    /// starts loading, finishes its own setup, then stays on its loading screen (no squads, no waves, no timers) until
    /// every other player reports their copy of the island ready. Everyone sees who is still loading. After two minutes
    /// it starts anyway, so a crashed player can't hold the rest forever.
    /// The campaign map works the same way: whenever the leader arrives on the map, or sends everyone a changed
    /// campaign (Next Turn, an upgrade), voting waits until every player has that map loaded (at most MapTimeout).
    /// While either wait is on, every player's screen is greyed out and takes no clicks.
    /// </summary>
    internal class LoadGate : MonoBehaviour
    {
        public const float Timeout = 120f;
        public const float MapTimeout = 90f;

        // Leader: who hasn't reported the current island ready yet.
        private static readonly HashSet<CSteamID> waiting = new HashSet<CSteamID>();
        private static int level = -1;
        private static float since, nextBroadcast;

        /// <summary>When the leader last stopped waiting for anyone to load (0: never had to).</summary>
        public static float ReleasedAt { get; private set; }
        // Everyone: the leader's latest list of players still loading (for the message on screen).
        private static readonly List<CSteamID> loading = new List<CSteamID>();
        private static float loadingHeardAt = -100f;
        // Watchers: the island we told the leader we're ready for, and when we last said so.
        private static int reportedLevel = -1;
        private static float nextReport;

        // Leader, the campaign map: who hasn't loaded the current map yet. A visit is one stay on the map (it goes up
        // each time the leader arrives there), the revision is the campaign every player has to have loaded.
        private static readonly HashSet<CSteamID> mapWaiting = new HashSet<CSteamID>();
        private static int mapVisit, mapRevision = -1;
        private static float mapSince, nextMapBroadcast;
        private static bool leaderOnMap;
        // Everyone: the leader's latest map wait (for the message on screen and for reporting in).
        private static readonly List<CSteamID> mapLoading = new List<CSteamID>();
        private static int heardVisit, heardRevision;
        private static float mapHeardAt = -100f, nextMapReport;

        private static SteamSession Session => SteamSession.Instance;

        /// <summary>Voting waits: someone hasn't loaded the campaign map yet (as the leader last said).</summary>
        public static bool MapWaiting => mapLoading.Count > 0 && Time.unscaledTime - mapHeardAt < 4f && Session && Session.InLobby;

        /// <summary>The island won't start: someone hasn't loaded it yet (as the leader last said).</summary>
        private static bool IslandWaiting => loading.Count > 0 && Time.unscaledTime - loadingHeardAt < 4f && Session && Session.InLobby;

        /// <summary>
        /// Someone is still loading the island or the map: every player's screen is greyed out and takes no clicks
        /// until everyone has it loaded. The leader's word is only trusted for 4 s, so a leader who vanishes can't
        /// leave anyone locked out.
        /// </summary>
        public static bool Blocking => IslandWaiting || MapWaiting;

        public static void Init()
        {
            Router.On(MsgType.MapReady, (sender, r) =>
            {
                int visit = r.ReadInt32(), revision = r.ReadInt32();
                if (visit != mapVisit || revision < mapRevision || !mapWaiting.Remove(sender)) return;
                ModLog.Info($"{SteamSession.NameOf(sender)} has loaded the campaign map" +
                    (mapWaiting.Count > 0 ? $"; still waiting for {Names(mapWaiting)}" : "; everyone is on the map"));
                BroadcastMap();
            });
            Router.On(MsgType.MapWait, (sender, r) =>
            {
                if (sender != Roles.DefenderId) return;
                int visit = r.ReadInt32(), revision = r.ReadInt32();
                var listed = new List<CSteamID>();
                for (int count = r.ReadByte(), i = 0; i < count; i++) listed.Add(new CSteamID(r.ReadUInt64()));
                HeardMap(visit, revision, listed);
            });
            Router.On(MsgType.BattleReady, (sender, r) =>
            {
                int ready = r.ReadInt32();
                if (ready != level || !waiting.Remove(sender)) return;
                ModLog.Info($"{SteamSession.NameOf(sender)} has loaded the island" +
                    (waiting.Count > 0 ? $"; still waiting for {Names(waiting)}" : "; everyone is in"));
                nextBroadcast = 0f;
                if (waiting.Count == 0)
                {
                    ReleasedAt = Time.unscaledTime;
                    Broadcast(); // everyone's "waiting for..." message goes away now
                }
            });
            Router.On(MsgType.LoadWait, (sender, r) =>
            {
                if (sender != Roles.DefenderId) return;
                int forLevel = r.ReadInt32();
                loading.Clear();
                for (int count = r.ReadByte(), i = 0; i < count; i++) loading.Add(new CSteamID(r.ReadUInt64()));
                loadingHeardAt = Time.unscaledTime;
                // Still listed although our island is ready: the report got lost, say it again.
                if (loading.Contains(Session.Me) && reportedLevel == forLevel && Time.unscaledTime >= nextReport) Report(forLevel);
            });
        }

        /// <summary>Leader, as its island starts loading (after the battle went out to everyone).</summary>
        public static void Begin(int levelIndex)
        {
            waiting.Clear();
            EndMapWait();
            level = levelIndex;
            since = Time.unscaledTime;
            nextBroadcast = 0f;
            if (!Session || !Session.InLobby) return;
            foreach (CSteamID member in Session.Members())
                if (member != Session.Me && Updater.IsCompatible(member)) waiting.Add(member);
            if (waiting.Count > 0) ModLog.Info($"Island {levelIndex}: waiting for {Names(waiting)} to load before it starts");
            else ReleasedAt = Time.unscaledTime;
        }

        /// <summary>Leader: keep the island from starting?</summary>
        public static bool Holding
        {
            get
            {
                if (waiting.Count == 0) return false;
                if (Session && Session.InLobby) waiting.RemoveWhere(member => !Session.Members().Contains(member));
                else waiting.Clear();
                if (waiting.Count > 0 && Time.unscaledTime - since > Timeout)
                {
                    ModLog.Warn($"Starting island {level} without {Names(waiting)}: still loading after {Timeout:0} s");
                    MultiplayerGame.Instance.Announce($"Starting without {Names(waiting)} (still loading)");
                    waiting.Clear();
                }
                if (waiting.Count == 0)
                {
                    ReleasedAt = Time.unscaledTime;
                    ModLog.Info($"Island {level}: everyone has loaded; starting");
                    Broadcast();
                }
                return waiting.Count > 0;
            }
        }

        private static void Broadcast()
        {
            if (!Session || !Session.InLobby) return;
            nextBroadcast = Time.unscaledTime + 1f;
            loading.Clear();
            loading.AddRange(waiting);
            loadingHeardAt = Time.unscaledTime;
            Session.Broadcast(Packet.Build(MsgType.LoadWait, w =>
            {
                w.Write(level);
                w.Write((byte)Mathf.Min(waiting.Count, 255));
                foreach (CSteamID member in waiting) w.Write(member.m_SteamID);
            }));
        }

        private static void Report(int levelIndex)
        {
            reportedLevel = levelIndex;
            nextReport = Time.unscaledTime + 2f;
            Session.Send(Roles.DefenderId, Packet.Build(MsgType.BattleReady, w => w.Write(levelIndex)));
        }

        // ---- The campaign map ---------------------------------------------------------------------

        /// <summary>Leader, every frame: start a map wait on arriving at the map, and again for each new campaign sent.</summary>
        private static void LeaderMap()
        {
            bool onMap = Session && Session.InLobby && Roles.IsDefender && PartyFlow.Deployed && IslandVote.LeaderOnMap();
            if (!onMap)
            {
                if (leaderOnMap) EndMapWait();
                leaderOnMap = false;
                return;
            }
            if (!leaderOnMap) { leaderOnMap = true; mapVisit++; StartMapWait(); }
            else if (CampaignSync.LoudRevision != mapRevision) StartMapWait();
            if (mapWaiting.Count > 0 && Time.unscaledTime >= nextMapBroadcast) BroadcastMap();
        }

        private static void StartMapWait()
        {
            mapRevision = CampaignSync.LoudRevision;
            mapSince = Time.unscaledTime;
            mapWaiting.Clear();
            foreach (CSteamID member in Session.Members())
                if (member != Session.Me && Updater.IsCompatible(member)) mapWaiting.Add(member);
            nextMapBroadcast = 0f;
        }

        private static void EndMapWait()
        {
            if (mapWaiting.Count == 0) return;
            mapWaiting.Clear();
            BroadcastMap();
        }

        /// <summary>Leader: is voting held up by someone still loading the map?</summary>
        public static bool MapHolding
        {
            get
            {
                if (mapWaiting.Count == 0) return false;
                if (Session && Session.InLobby) mapWaiting.RemoveWhere(member => !Session.Members().Contains(member));
                else mapWaiting.Clear();
                if (mapWaiting.Count > 0 && Time.unscaledTime - mapSince > MapTimeout)
                {
                    ModLog.Warn($"Voting without {Names(mapWaiting)}: still loading the map after {MapTimeout:0} s");
                    MultiplayerGame.Instance.Announce($"Continuing without {Names(mapWaiting)} (still loading the map)");
                    mapWaiting.Clear();
                }
                if (mapWaiting.Count == 0) BroadcastMap();
                return mapWaiting.Count > 0;
            }
        }

        private static void BroadcastMap()
        {
            if (!Session || !Session.InLobby) return;
            nextMapBroadcast = Time.unscaledTime + 1f;
            HeardMap(mapVisit, mapRevision, new List<CSteamID>(mapWaiting));
            var listed = new List<CSteamID>(mapWaiting);
            int visit = mapVisit, revision = mapRevision;
            Session.Broadcast(Packet.Build(MsgType.MapWait, w =>
            {
                w.Write(visit);
                w.Write(revision);
                w.Write((byte)Mathf.Min(listed.Count, 255));
                foreach (CSteamID member in listed) w.Write(member.m_SteamID);
            }));
        }

        private static void HeardMap(int visit, int revision, List<CSteamID> listed)
        {
            heardVisit = visit;
            heardRevision = revision;
            mapLoading.Clear();
            mapLoading.AddRange(listed);
            mapHeardAt = Time.unscaledTime;
            if (mapLoading.Contains(Session.Me)) nextMapReport = Mathf.Min(nextMapReport, Time.unscaledTime);
        }

        /// <summary>Watchers, every frame: once our map has loaded the campaign the leader wants, say so (again every 2 s while still listed).</summary>
        private static void ReportMap()
        {
            if (!Session || !Session.InLobby || !Roles.WatchesBattle || !MapWaiting || !mapLoading.Contains(Session.Me)) return;
            if (!SpectatorSession.MapSettled || CampaignSync.AppliedRevision < heardRevision || Time.unscaledTime < nextMapReport) return;
            nextMapReport = Time.unscaledTime + 2f;
            int visit = heardVisit, revision = CampaignSync.AppliedRevision;
            Session.Send(Roles.DefenderId, Packet.Build(MsgType.MapReady, w => { w.Write(visit); w.Write(revision); }));
        }

        private static string Names(IEnumerable<CSteamID> players)
        {
            var names = new List<string>();
            foreach (CSteamID player in players) names.Add(SteamSession.NameOf(player));
            return string.Join(", ", names.ToArray());
        }

        // ---- Every frame ------------------------------------------------------------------------

        // The shade only fades in after a moment: a wait everyone clears at once (players already on the map report in
        // straight away) blocks clicks without flashing the screen.
        private const float ShadeDelay = 0.25f, ShadeFade = 0.25f, ShadeAlpha = 0.6f;

        private GameObject overlay;
        private Image shade;
        private Text text;
        private float blockedSince = -1f;
        private bool blockedOnIsland;

        private void Update()
        {
            if (waiting.Count > 0 && Time.unscaledTime >= nextBroadcast) Broadcast();
            // Watchers: our copy of the island is ready; tell the leader once per island.
            if (Session && Session.InLobby && Roles.WatchesBattle && SpectatorSession.Active && SpectatorSession.IslandReady())
            {
                var manager = Singleton<IslandGameplayManager>.instance;
                int current = manager && manager.island ? manager.island.levelNode.index : -1;
                if (current >= 0 && current != reportedLevel) Report(current);
            }
            else if (!SpectatorSession.Active) reportedLevel = -1;
            LeaderMap();
            ReportMap();
            ShowWaiting();
        }

        /// <summary>
        /// While anyone is still loading (Blocking): a grey shade over everything, on top of every other screen, that
        /// swallows every click (the game's island and map clicks go through the same UI raycast), with who it's
        /// waiting for in the middle.
        /// </summary>
        private void ShowWaiting()
        {
            bool island = IslandWaiting, blocking = island || MapWaiting;
            if (blocking && blockedSince < 0f) { blockedSince = Time.unscaledTime; blockedOnIsland = island; }
            if (!blocking)
            {
                if (blockedSince >= 0f && Time.unscaledTime - blockedSince >= 1f)
                    ModLog.Info($"Waited {Time.unscaledTime - blockedSince:0.0} s for everyone to load the {(blockedOnIsland ? "island" : "map")}");
                blockedSince = -1f;
                if (overlay && overlay.activeSelf) overlay.SetActive(false);
                return;
            }
            if (!overlay) Build();
            if (!overlay.activeSelf) overlay.SetActive(true);
            float shown = Mathf.Clamp01((Time.unscaledTime - blockedSince - ShadeDelay) / ShadeFade);
            // Never fully transparent: the shade has to stay a raycast target to block clicks.
            shade.color = new Color(0.12f, 0.12f, 0.12f, Mathf.Max(0.01f, ShadeAlpha * shown));
            text.color = new Color(1f, 1f, 1f, shown);
            var others = new List<CSteamID>(island ? loading : mapLoading);
            bool me = others.Remove(Session.Me);
            string what = island ? "the island" : "the map";
            text.text = me ? $"Loading {what}... everyone else is waiting for you" : $"Waiting for {Names(others)} to load {what}...";
        }

        private void Build()
        {
            overlay = new GameObject("BNMP Load gate");
            DontDestroyOnLoad(overlay);
            var canvas = overlay.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31000; // above the loading screen, the sail-in bars (30000) and every mod screen
            var scaler = overlay.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            overlay.AddComponent<GraphicRaycaster>();

            var shadeObject = new GameObject("Shade", typeof(RectTransform));
            shadeObject.transform.SetParent(overlay.transform, false);
            var shadeRect = (RectTransform)shadeObject.transform;
            shadeRect.anchorMin = Vector2.zero;
            shadeRect.anchorMax = Vector2.one;
            shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;
            shade = shadeObject.AddComponent<Image>();
            shade.raycastTarget = true;

            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            foreach (Voxels.TowerDefense.UI.MainMenu menu in Resources.FindObjectsOfTypeAll<Voxels.TowerDefense.UI.MainMenu>())
            {
                var prefab = Traverse.Create(menu).Field<ButtonWidget>("textButtonPrefab").Value;
                if (prefab && prefab.label && prefab.label.font) { font = prefab.label.font; break; }
            }
            text = UI.NativeWidgets.Text(overlay.transform, font, "", 34);
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(10f, 40f);
        }
    }

    /// <summary>The leader's island finishes setting up, then waits on the loading screen for everyone (LoadGate).</summary>
    [HarmonyPatch(typeof(IslandGameplayManager), "PlayIslandRoutine")]
    internal static class LoadGatePatch
    {
        private static void Postfix(ref IEnumerator<GenInfo> __result)
        {
            if (!SpectatorSession.Active) __result = Gated(__result);
        }

        private static IEnumerator<GenInfo> Gated(IEnumerator<GenInfo> original)
        {
            while (original.MoveNext()) yield return original.Current;
            // One step per frame while waiting (forceInterrupt hands the frame back to the loading screen).
            while (LoadGate.Holding) yield return new GenInfo("Waiting for players to load", GenInfo.Mode.forceInterrupt);
        }
    }
}
