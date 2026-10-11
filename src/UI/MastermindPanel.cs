using System;
using System.Collections.Generic;
using BadNorthMP.Game;
using BadNorthMP.Net;
using BadNorthMP.Spectator;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    /// <summary>
    /// The mastermind's battle screen, built from the game's own pieces and kept to the edges of the island:
    /// - top: the wave and when it sails (white text, like the game's words over the island); the war chest coin;
    /// - bottom: one round ability button per Viking type (left-click adds, right-click removes, shift for five;
    ///   the bubble shows how many), and Send now, whose ring fills up as the wave's timer runs out;
    /// - bottom left: commander buttons (deploy, select, then click a tile or a house);
    /// - landing: shore tiles where ships fit glow blue; click one and it turns gold.
    /// Clicks on the island come from the game's own pointer (it already ignores camera drags and UI), and the
    /// war chest opens over everything (WarChestPopup).
    /// </summary>
    internal class MastermindPanel : MonoBehaviour
    {
        public static MastermindPanel Instance { get; private set; }
        private void Awake() => Instance = this;

        /// <summary>The native upgrades button on the map opens the war chest for the mastermind.</summary>
        public static void OpenMapUpgrades()
        {
            if (Roles.IsMastermind) WarChestPopup.Open(battle: false);
        }

        private static readonly Color Gold = new Color(1f, 0.83f, 0.42f, 1f);
        private static readonly Color Warning = new Color(1f, 0.62f, 0.5f, 1f);

        // ---- Tile tints (NavSpotter reads them every frame, so changing the colour animates) ---------

        private sealed class TileTint : NavSpot.IHighlight
        {
            public Color color;
            public Color highlightColor => color;
            public TileTint(Color color) { this.color = color; }
        }
        private readonly TileTint landTint = new TileTint(Color.clear);
        private readonly TileTint hoverTint = new TileTint(new Color(0.9f, 1f, 1f, 0.85f));
        private readonly TileTint chosenTint = new TileTint(new Color(1f, 0.76f, 0.15f, 0.95f));
        private readonly TileTint plannedTint = new TileTint(new Color(1f, 0.6f, 0.15f, 0.6f));
        private readonly TileTint orderTint = new TileTint(new Color(1f, 0.42f, 0.28f, 0.85f));

        // ---- Widgets ----------------------------------------------------------------------------

        private sealed class UnitCard { public VikingReference type; public NativeRoundButton button; }

        // The wave being planned: per landing beach (index into BeachChoices), the paid Vikings per army-bar card.
        // The selected beach (beachIndex) is the one the army bar fills.
        private readonly Dictionary<int, int[]> landings = new Dictionary<int, int[]>();
        private readonly Dictionary<int, NavSpot> plannedSpots = new Dictionary<int, NavSpot>();
        private readonly Dictionary<int, Text> landingLabels = new Dictionary<int, Text>();

        private Font font;
        private GameObject canvasObject, battleRoot, mapRoot;
        private RectTransform canvasRect, landPin, chestGainRect;
        private GraphicRaycaster raycaster;
        private Text waveText, waveSub, statusText, costText, hintText, chestAmount, chestGain, mapChestAmount;
        private RectTransform armyRow, commanderRow;
        private readonly List<UnitCard> cards = new List<UnitCard>();
        private readonly List<NativeRoundButton> commanderButtons = new List<NativeRoundButton>();
        private readonly List<GameObject> commanderRests = new List<GameObject>();
        private readonly List<GameObject> commanderSkulls = new List<GameObject>();
        private readonly Dictionary<int, NativeRoundButton> commanderMarkers = new Dictionary<int, NativeRoundButton>();

        // ---- State ------------------------------------------------------------------------------

        private Island builtFor;
        private bool battleBuilt, battleNative, mapBuilt;
        private PointerRationalizer pointer;
        private int beachIndex = -1, shownWave = -1;
        // Commanders taking orders, in the order they were picked (Shift adds, Alt+drag box-selects).
        private readonly List<int> selected = new List<int>();
        private bool Ordering => selected.Count > 0;
        private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        private GameObject marqueeCatcher;
        private RectTransform marqueeBox;
        private Vector2 marqueeStart;
        private bool marqueeDragging;
        private bool planDirty, leftPressed;
        private Vector3 leftPressAt;
        private float planChangedAt, lastToast = -100f, lastToastTextAt = -100f, nextLandHint, chestGainAt = -100f;
        private string lastToastText;
        private readonly List<NavSpot> landTiles = new List<NavSpot>();
        private NavSpot chosenSpot, hoveredSpot;
        private readonly Dictionary<int, NavSpot> orderedTiles = new Dictionary<int, NavSpot>();
        private CampaignMapUI nativeMap;
        private float nextMapSearch, nextReferenceSearch;
        private readonly Dictionary<int, VikingReference> references = new Dictionary<int, VikingReference>();
        private readonly List<RaycastResult> hits = new List<RaycastResult>();

        /// <summary>The game's campaign map UI, searched for at most once a second until found.</summary>
        private CampaignMapUI NativeMap()
        {
            if (!nativeMap && Time.unscaledTime >= nextMapSearch)
            {
                nextMapSearch = Time.unscaledTime + 1f;
                nativeMap = FindObjectOfType<CampaignMapUI>();
            }
            return nativeMap;
        }

        private static string Name(int type) => VikingCommanders.TypeName(type);

        /// <summary>A loaded reference with an icon for a Viking type (commanders can be any unlocked type).</summary>
        private VikingReference Reference(int type)
        {
            if (references.TryGetValue(type, out VikingReference found) && found) return found;
            if (Time.unscaledTime < nextReferenceSearch) return null;
            nextReferenceSearch = Time.unscaledTime + 2f;
            foreach (VikingReference candidate in Resources.FindObjectsOfTypeAll<VikingReference>())
                if (candidate && UnitIcons.Has(candidate) && MastermindUpgrades.ValidType(candidate.type))
                    references[(int)candidate.type] = candidate;
            return references.TryGetValue(type, out found) ? found : null;
        }

        // ---- Frame ------------------------------------------------------------------------------

        private void Update()
        {
            bool battle = Roles.IsMastermind && SpectatorSession.Active && SpectatorSession.IslandReady();
            var stack = Singleton<Voxels.TowerDefense.Stack>.instance;
            // Cheap checks first: Singleton lookups fall back to a full scene search when nothing is found.
            bool map = Roles.IsMastermind && SpectatorSession.ViewingCampaign && stack &&
                !stack.stateLoading.gameObject.activeInHierarchy && Profile.campaign != null && !GameAccess.CurrentIsland;
            if (map)
            {
                var campaigns = Singleton<CampaignManager>.instance;
                CampaignMapUI mapUI = NativeMap();
                map = campaigns && campaigns.campaign && mapUI && mapUI.gameObject.activeInHierarchy;
            }
            // The war chest popup lives as long as the campaign does (the map UI may hide behind it).
            bool campaign = battle || (Roles.IsMastermind && SpectatorSession.ViewingCampaign && !GameAccess.CurrentIsland);
            if (campaign) WarChestPopup.Refresh(battle);
            else WarChestPopup.Close();
            if (!battle && !map)
            {
                ClearWorld();
                Unhook();
                builtFor = null;
                if (canvasObject && canvasObject.activeSelf) canvasObject.SetActive(false);
                return;
            }
            if (!canvasObject) BuildCanvas();
            if (!canvasObject.activeSelf) canvasObject.SetActive(true);
            // Built on first use, and again once the game's button graphics have loaded (they come with the island UI).
            if (battle && (!battleBuilt || (!battleNative && NativeWidgets.HasRoundButton))) BuildBattle();
            if (map && !mapBuilt) BuildMap();

            Island island = GameAccess.CurrentIsland;
            if (battle && builtFor != island) { ClearWorld(); BuildArmy(island); builtFor = island; }
            if (!battle && builtFor) { ClearWorld(); builtFor = null; }
            bool chest = WarChestPopup.IsOpen;
            battleRoot.SetActive(battle && !chest);
            mapRoot.SetActive(map && !chest);

            if (battle)
            {
                HookPointer();
                if (!chest) Keys();
                if (!pointer && !chest) FallbackClicks();
                RefreshBattle(island);
                RefreshLandPin();
                RefreshCommanderMarkers();
                RefreshOrderedTiles();
            }
            else
            {
                Unhook();
                var state = MastermindNet.HasFreshState ? MastermindNet.State.upgrades : null;
                mapChestAmount.text = state != null ? Mathf.FloorToInt(state.gold).ToString() : "-";
            }
            Toast();
        }

        /// <summary>
        /// Replies from the leader appear as the game's banner (or a map notice). War chest rewards during the fight
        /// only float a "+N" under the coin, so kills don't flood the screen.
        /// </summary>
        private void Toast()
        {
            if (MastermindNet.LastResultTime <= lastToast) return;
            lastToast = MastermindNet.LastResultTime;
            string text = MastermindNet.LastResult;
            if (string.IsNullOrEmpty(text) || text.EndsWith("...")) return;
            if (text.StartsWith("+") && !text.Contains("island"))
            {
                if (!chestGain) return;
                int end = text.IndexOf(' ');
                chestGain.text = end > 0 ? text.Substring(0, end) : text;
                chestGainAt = Time.unscaledTime;
                FabricWrapper.PostEvent("UI/InGame/CoinAdd");
                return;
            }
            if (WarChestPopup.IsOpen) return; // the popup shows the latest reply itself
            if (text.StartsWith("Can't: ")) text = text.Substring(7);
            // The same refusal again (an over-budget plan resent) doesn't need a second banner.
            if (text.Length == 0 || (text == lastToastText && Time.unscaledTime - lastToastTextAt < 5f)) return;
            lastToastText = text;
            lastToastTextAt = Time.unscaledTime;
            if (char.IsLower(text[0])) text = char.ToUpper(text[0]) + text.Substring(1);
            GameNotify.Post(text, 3f);
        }

        // ---- Island input -----------------------------------------------------------------------

        /// <summary>
        /// Listen to the game's own pointer: it sits behind every other UI as a full-screen catcher, reports clicks
        /// only (not camera drags), and never fires through buttons or popups.
        /// </summary>
        private void HookPointer()
        {
            var manager = Singleton<IslandGameplayManager>.instance;
            PointerRationalizer found = manager ? manager.pointerRationalizer : null;
            if (found == pointer) return;
            Unhook();
            pointer = found;
            if (pointer) pointer.onClick += IslandClicked;
        }

        private void Unhook()
        {
            if (pointer) pointer.onClick -= IslandClicked;
            pointer = null;
        }

        /// <summary>Only if the game's pointer can't be found: a press and release without dragging, not over our UI.</summary>
        private void FallbackClicks()
        {
            if (Input.GetMouseButtonDown(0)) { leftPressed = !OverOwnUi(Input.mousePosition); leftPressAt = Input.mousePosition; }
            if (leftPressed && Input.GetMouseButtonUp(0))
            {
                leftPressed = false;
                if ((Input.mousePosition - leftPressAt).magnitude < 12f) IslandClicked(PointerEventData.InputButton.Left, Input.mousePosition);
            }
            if (Input.GetMouseButtonDown(1) && !OverOwnUi(Input.mousePosition)) IslandClicked(PointerEventData.InputButton.Right, Input.mousePosition);
        }

        private bool OverOwnUi(Vector2 position)
        {
            if (!EventSystem.current || !raycaster) return false;
            hits.Clear();
            raycaster.Raycast(new PointerEventData(EventSystem.current) { position = position }, hits);
            return hits.Count > 0;
        }

        /// <summary>Is the mouse over the island itself (not over any UI, not dragging the camera)?</summary>
        private bool PointerOverIsland() => pointer
            ? pointer.state == PointerRationalizer.State.Hover
            : !OverOwnUi(Input.mousePosition);

        private void Keys()
        {
            // Holding Alt puts a catcher over the island, so a drag draws a selection box instead of turning the camera.
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (marqueeCatcher && marqueeCatcher.activeSelf != (alt || marqueeDragging)) marqueeCatcher.SetActive(alt || marqueeDragging);
            if (!Ordering || LoadGate.Blocking) return;
            if (Input.GetKeyDown(KeyCode.Escape)) Deselect();
            else if (Input.GetKeyDown(KeyCode.R)) Roam();
        }

        private void IslandClicked(PointerEventData.InputButton button, Vector2 position)
        {
            if (LoadGate.Blocking) return; // someone is still loading: no orders yet
            Island island = builtFor;
            if (!island || WarChestPopup.IsOpen || !battleRoot || !battleRoot.activeInHierarchy) return;
            if (button == PointerEventData.InputButton.Right)
            {
                if (Ordering) Deselect();
                else if (!MastermindNet.State.done) ClearLandingAt(island, position);
                return;
            }
            if (button != PointerEventData.InputButton.Left || SelectCommanderAt(position)) return;
            if (Ordering) { OrderCommanders(island, position); return; }
            if (MastermindNet.State.done) return;
            NavSpot target = island.navSpotter.NavSpotCast(position);
            if (!target) return;
            int index = BeachChoices.Get(island).FindIndex(c => c.tileIndex == target.index);
            if (index >= 0) { SelectLanding(index); return; }
            FabricWrapper.PostEvent("UI/InGame/Error");
            if (Time.unscaledTime >= nextLandHint)
            {
                nextLandHint = Time.unscaledTime + 6f;
                GameNotify.Post("Ships can only land on the glowing shore", 2.5f);
            }
        }

        private bool SelectCommanderAt(Vector2 mouse)
        {
            var camera = Singleton<LevelCamera>.instance;
            var state = MastermindNet.State.upgrades;
            if (!camera || !camera.cameraRef || state == null) return false;
            foreach (var commander in state.commanders)
                // Clicking right by a commander that's already taking orders moves it there, unless Shift (un)picks it.
                if (commander.status == CommanderStatus.Deployed && (Shift || !selected.Contains(commander.id)) &&
                    CommanderPosition(commander.id, out Vector3 position))
                {
                    Vector3 screen = camera.cameraRef.WorldToScreenPoint(position);
                    if (screen.z > 0f && ((Vector2)screen - mouse).magnitude < 40f) { Select(commander.id, Shift); return true; }
                }
            return false;
        }

        /// <summary>Where a deployed commander's living Vikings are (their mirrored copies).</summary>
        private static bool CommanderPosition(int id, out Vector3 position) => PuppetWorld.CommanderPosition(id, out position);

        private static bool CommanderKnown(int id) => PuppetWorld.CommanderKnown(id);

        /// <summary>Pick a commander alone, or with <paramref name="additive"/> add/remove it from the picked ones.</summary>
        private void Select(int id, bool additive)
        {
            if (additive) { if (!selected.Remove(id)) selected.Add(id); }
            else if (selected.Count == 1 && selected[0] == id) { Deselect(); return; }
            else { selected.Clear(); selected.Add(id); }
            FabricWrapper.PostEvent(selected.Contains(id) ? "UI/InGame/UnitSelect" : "UI/InGame/UnitDeselect");
        }

        private void Deselect()
        {
            if (selected.Count == 0) return;
            selected.Clear();
            FabricWrapper.PostEvent("UI/InGame/UnitDeselect");
        }

        private void Roam()
        {
            foreach (int id in selected)
            {
                MastermindNet.RequestCommanderOrder(id, VikingCommanders.OrderRelease, 0);
                SetOrderedTile(id, null);
            }
            Deselect();
        }

        // ---- Box select (Alt + drag) ------------------------------------------------------------

        internal void MarqueeDown(Vector2 position)
        {
            marqueeStart = position;
            marqueeDragging = true;
        }

        internal void MarqueeDrag(Vector2 position)
        {
            if (!marqueeDragging || !marqueeBox) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Vector2.Min(marqueeStart, position), null, out Vector2 min);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Vector2.Max(marqueeStart, position), null, out Vector2 max);
            marqueeBox.anchoredPosition = min;
            marqueeBox.sizeDelta = max - min;
            if (!marqueeBox.gameObject.activeSelf) marqueeBox.gameObject.SetActive(true);
        }

        internal void MarqueeUp(Vector2 position)
        {
            if (!marqueeDragging) return;
            marqueeDragging = false;
            if (marqueeBox) marqueeBox.gameObject.SetActive(false);
            Rect box = Rect.MinMaxRect(Mathf.Min(marqueeStart.x, position.x), Mathf.Min(marqueeStart.y, position.y),
                Mathf.Max(marqueeStart.x, position.x), Mathf.Max(marqueeStart.y, position.y));
            if (box.width < 8f && box.height < 8f) return;
            var camera = Singleton<LevelCamera>.instance;
            var state = MastermindNet.State.upgrades;
            if (!camera || !camera.cameraRef || state == null) return;
            if (!Shift) selected.Clear();
            foreach (var commander in state.commanders)
                if (commander.status == CommanderStatus.Deployed && !selected.Contains(commander.id) &&
                    CommanderPosition(commander.id, out Vector3 world))
                {
                    Vector3 screen = camera.cameraRef.WorldToScreenPoint(world);
                    if (screen.z > 0f && box.Contains(screen)) selected.Add(commander.id);
                }
            FabricWrapper.PostEvent(Ordering ? "UI/InGame/UnitSelect" : "UI/InGame/UnitDeselect");
        }

        /// <summary>
        /// With commanders picked: a house near the click means they all burn it; otherwise the first goes to the
        /// tile and each other one to a free tile next to it (two commanders never hold the same tile).
        /// </summary>
        private void OrderCommanders(Island island, Vector2 position)
        {
            NavSpot spot = island.navSpotter.NavSpotCast(position);
            if (!spot) return;
            Vector3 point = spot.navPos.wPos;
            int house = -1;
            float nearest = 1.1f;
            House[] houses = island.village ? island.village.houses : null;
            for (int i = 0; houses != null && i < houses.Length; i++)
            {
                if (!houses[i] || houses[i].destroyed) continue;
                Vector3 offset = houses[i].transform.position - point;
                offset.y = 0f;
                if (offset.magnitude < nearest) { nearest = offset.magnitude; house = i; }
            }
            if (house >= 0)
                foreach (int id in selected)
                {
                    MastermindNet.RequestCommanderOrder(id, VikingCommanders.OrderBurn, house);
                    SetOrderedTile(id, null);
                }
            else
            {
                // Tiles other (unpicked) commanders were sent to are taken.
                var taken = new HashSet<NavSpot>();
                foreach (var held in orderedTiles) if (!selected.Contains(held.Key) && held.Value) taken.Add(held.Value);
                foreach (int id in selected)
                {
                    NavSpot tile = !taken.Contains(spot) ? spot : FreeNeighbour(spot, taken);
                    if (!tile) break;
                    taken.Add(tile);
                    MastermindNet.RequestCommanderOrder(id, VikingCommanders.OrderMove, island.navSpotter.navSpots.IndexOf(tile));
                    SetOrderedTile(id, tile);
                }
            }
            FabricWrapper.PostEvent("UI/InGame/UnitMove");
        }

        private static NavSpot FreeNeighbour(NavSpot center, HashSet<NavSpot> taken)
        {
            if (center.neighbours == null) return null;
            foreach (NavSpot neighbour in center.neighbours)
                if (neighbour && !taken.Contains(neighbour)) return neighbour;
            return null;
        }

        private void SetOrderedTile(int commander, NavSpot spot)
        {
            if (orderedTiles.TryGetValue(commander, out NavSpot old) && old)
            {
                orderedTiles.Remove(commander);
                if (!orderedTiles.ContainsValue(old)) old.RemoveHighlight(orderTint);
            }
            if (spot) { orderedTiles[commander] = spot; spot.AddHighlight(orderTint); }
        }

        private void RefreshOrderedTiles()
        {
            var state = MastermindNet.State.upgrades;
            var gone = new List<int>();
            foreach (int id in orderedTiles.Keys)
                if (state == null || !state.commanders.Exists(c => c.id == id && c.status == CommanderStatus.Deployed)) gone.Add(id);
            foreach (int id in gone) SetOrderedTile(id, null);
        }

        // ---- Battle screen ----------------------------------------------------------------------

        private void RefreshBattle(Island island)
        {
            MasterState s = MastermindNet.State;
            bool fresh = MastermindNet.HasFreshState;
            bool live = fresh && s.inBattle && !s.done;
            float chest = fresh && s.upgrades != null ? s.upgrades.gold : 0f;

            // A new wave opened: its plan starts empty and the timer ring starts over.
            if (fresh && s.waveIndex != shownWave)
            {
                if (shownWave >= 0) { landings.Clear(); PlanChanged(); }
                shownWave = s.waveIndex;
                planDirty = false;
            }
            SyncPlan();

            waveText.text = !fresh ? "Waiting for the defenders" : s.done ? "Every wave has sailed"
                : s.FinalWave ? "Final wave" : $"Wave {s.waveIndex + 1} of {s.waveCount}";
            waveSub.text = !fresh ? "" : s.done ? "Finish them off"
                : !s.inBattle ? $"{Mathf.FloorToInt(s.waveBudget)} gold to spend  ·  the fight hasn't started"
                : s.landingHold ? $"{Mathf.FloorToInt(s.waveBudget)} gold to spend  ·  sails as soon as the defenders have landed"
                : $"{Mathf.FloorToInt(s.waveBudget)} gold to spend  ·  sails in {Mathf.CeilToInt(s.secondsToDeparture)}s";
            chestAmount.text = Mathf.FloorToInt(chest).ToString();
            float gainAge = Time.unscaledTime - chestGainAt;
            chestGain.gameObject.SetActive(gainAge < 1.5f);
            if (gainAge < 1.5f)
            {
                chestGain.color = new Color(Gold.r, Gold.g, Gold.b, 1f - gainAge / 1.5f);
                chestGainRect.anchoredPosition = new Vector2(0f, -24f - 18f * gainAge);
            }

            List<BeachChoice> beaches = BeachChoices.Get(island);
            if (beachIndex >= beaches.Count) beachIndex = -1;
            int cost = Cost(), vikings = 0, budget = Mathf.FloorToInt(s.waveBudget);
            foreach (int[] counts in landings.Values) vikings += Crew(counts, s);
            int[] here = beachIndex >= 0 && landings.TryGetValue(beachIndex, out int[] chosen) ? chosen : null;
            for (int i = 0; i < cards.Count; i++)
            {
                int count = here != null ? here[i] : 0;
                cards[i].button.SetIcon(UnitIcons.Of(cards[i].type)); // its picture, once the game has one
                cards[i].button.SetCount(count > 0 ? count.ToString() : null);
                cards[i].button.Available = beachIndex >= 0 && (count > 0 || cost + Economy.UnitCost(cards[i].type) <= s.waveBudget + 0.001f);
            }
            bool affordable = cost <= s.waveBudget + 0.001f;
            int planned = PlannedLandingCount();
            armyRow.gameObject.SetActive(fresh && !s.done);
            // The wave's timer: its ring fills up as departure nears. Waves only ever sail on the timer.

            // What this wave costs, always in view.
            costText.gameObject.SetActive(fresh && !s.done);
            costText.text = $"<color=#{(affordable ? "FFD36B" : "FF9E80")}>{cost}</color> / {budget} gold in this wave" +
                (affordable ? $"   ·   {budget - cost} left" : "   ·   over budget");

            statusText.color = Color.white;
            if (!fresh || s.done) statusText.text = "";
            else if (!affordable) { statusText.text = "Over budget: right-click a Viking to remove one"; statusText.color = Warning; }
            else if (beachIndex < 0) statusText.text = "Click a glowing shore tile to plan a landing there";
            else if (planned == 0) statusText.text = "Click a Viking to add one here (right-click removes). If you plan nothing, a random wave sails";
            else statusText.text = $"{vikings} Vikings on {planned} {(planned == 1 ? "beach" : "beaches")}" +
                (planned < EconomyConfig.MaxLandingsPerWave ? "   ·   click another shore tile to land more there" : "") +
                "   ·   right-click a gold tile to clear it";

            RefreshCommanderButtons(s, live);
            UpdateTileHighlights(island, beaches);
        }

        /// <summary>
        /// Landing shows on the island itself. Ship-ready shore tiles breathe blue until a landing is picked, then
        /// fade back so the island stays readable; the pick is gold. While a commander takes orders they disappear.
        /// </summary>
        private void UpdateTileHighlights(Island island, List<BeachChoice> beaches)
        {
            if (landTiles.Count == 0)
                foreach (var choice in beaches)
                {
                    NavSpot spot = island.navSpotter.navSpots.Find(n => n.index == choice.tileIndex);
                    if (spot) { spot.AddHighlight(landTint); landTiles.Add(spot); }
                }
            bool ordering = Ordering;
            bool planning = !MastermindNet.State.done;
            float breathe = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.6f);
            float alpha = ordering || !planning ? 0f : beachIndex < 0 ? Mathf.Lerp(0.35f, 0.7f, breathe) : 0.25f;
            landTint.color = new Color(0.3f, 0.82f, 1f, alpha);

            NavSpot next = beachIndex < 0 ? null : island.navSpotter.navSpots.Find(n => n.index == beaches[beachIndex].tileIndex);
            if (next != chosenSpot)
            {
                if (chosenSpot) chosenSpot.RemoveHighlight(chosenTint);
                chosenSpot = next;
                if (chosenSpot) chosenSpot.AddHighlight(chosenTint);
            }
            chosenTint.color = new Color(1f, 0.76f, 0.15f, ordering ? 0.35f : 0.95f);

            // Every other beach with Vikings planned: a softer gold.
            var wanted = new HashSet<int>();
            foreach (var landing in landings) if (landing.Key != beachIndex && Any(landing.Value)) wanted.Add(landing.Key);
            var drop = new List<int>();
            foreach (var planned in plannedSpots) if (!wanted.Contains(planned.Key)) drop.Add(planned.Key);
            foreach (int beach in drop) { if (plannedSpots[beach]) plannedSpots[beach].RemoveHighlight(plannedTint); plannedSpots.Remove(beach); }
            foreach (int beach in wanted)
            {
                if (plannedSpots.ContainsKey(beach) || beach >= beaches.Count) continue;
                NavSpot spot = island.navSpotter.navSpots.Find(n => n.index == beaches[beach].tileIndex);
                if (spot) { spot.AddHighlight(plannedTint); plannedSpots[beach] = spot; }
            }
            plannedTint.color = new Color(1f, 0.6f, 0.15f, ordering ? 0.25f : 0.6f);

            // Hover: any tile while a commander takes orders, otherwise only tiles ships can land on.
            NavSpot target = !WarChestPopup.IsOpen && PointerOverIsland() ? island.navSpotter.NavSpotCast(Input.mousePosition) : null;
            NavSpot hover = target && (ordering || (planning && landTiles.Contains(target))) && target != chosenSpot ? target : null;
            if (hover != hoveredSpot)
            {
                if (hoveredSpot) hoveredSpot.RemoveHighlight(hoverTint);
                hoveredSpot = hover;
                if (hoveredSpot) { hoveredSpot.AddHighlight(hoverTint); if (!ordering) FabricWrapper.PostEvent("UI/InGame/TileHover"); }
            }
        }

        /// <summary>Pick the beach the army bar fills (a new landing, or one already planned).</summary>
        private void SelectLanding(int index)
        {
            if (index < 0 || index >= BeachChoices.Get(builtFor).Count) return;
            PruneEmptyLandings();
            if (!landings.ContainsKey(index) && PlannedLandingCount() >= EconomyConfig.MaxLandingsPerWave)
            {
                FabricWrapper.PostEvent("UI/InGame/Error");
                GameNotify.Post($"A wave can land on at most {EconomyConfig.MaxLandingsPerWave} beaches", 2.5f);
                return;
            }
            beachIndex = index;
            FabricWrapper.PostEvent("UI/InGame/UnitMarkPlace");
        }

        /// <summary>Right-click on a planned beach: its Vikings come off the plan.</summary>
        private void ClearLandingAt(Island island, Vector2 position)
        {
            NavSpot target = island.navSpotter.NavSpotCast(position);
            int index = target ? BeachChoices.Get(island).FindIndex(c => c.tileIndex == target.index) : -1;
            if (index < 0 || !landings.TryGetValue(index, out int[] counts) || !Any(counts)) return;
            landings.Remove(index);
            PlanChanged();
            FabricWrapper.PostEvent("UI/InGame/UnitDeselect");
        }

        private void PruneEmptyLandings()
        {
            var empty = new List<int>();
            foreach (var landing in landings) if (!Any(landing.Value)) empty.Add(landing.Key);
            foreach (int beach in empty) landings.Remove(beach);
        }

        private int PlannedLandingCount()
        {
            int count = 0;
            foreach (int[] counts in landings.Values) if (Any(counts)) count++;
            return count;
        }

        private static bool Any(int[] counts)
        {
            foreach (int count in counts) if (count > 0) return true;
            return false;
        }

        /// <summary>The whole plan's gold.</summary>
        private int Cost()
        {
            int cost = 0;
            foreach (int[] counts in landings.Values)
                for (int i = 0; i < counts.Length && i < cards.Count; i++) cost += counts[i] * Economy.UnitCost(cards[i].type);
            return cost;
        }

        private int Cost(int[] counts)
        {
            int cost = 0;
            for (int i = 0; i < counts.Length && i < cards.Count; i++) cost += counts[i] * Economy.UnitCost(cards[i].type);
            return cost;
        }

        /// <summary>Vikings that land, count upgrades included.</summary>
        private int Crew(int[] counts, MasterState s)
        {
            int vikings = 0;
            for (int i = 0; i < counts.Length && i < cards.Count; i++)
            {
                int level = s.upgrades != null ? s.upgrades.Level(cards[i].type.type, UnitUpgrade.Count) : 0;
                vikings += MastermindUpgrades.CrewCount(counts[i], level);
            }
            return vikings;
        }

        /// <summary>
        /// A gold pin bobbing over the selected landing tile, and over every planned beach what lands there:
        /// "6 Vikings · 24 gold".
        /// </summary>
        private void RefreshLandPin()
        {
            var camera = Singleton<LevelCamera>.instance;
            List<BeachChoice> beaches = BeachChoices.Get(builtFor);
            bool visible = camera && camera.cameraRef && !Ordering && !MastermindNet.State.done;
            bool show = visible && beachIndex >= 0 && beachIndex < beaches.Count;
            Vector3 screen = show ? camera.cameraRef.WorldToScreenPoint(beaches[beachIndex].center) : Vector3.zero;
            show &= OnScreen(screen);
            landPin.gameObject.SetActive(show);
            if (show)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
                landPin.anchoredPosition = local + new Vector2(0f, 34f + 5f * Mathf.Sin(Time.unscaledTime * 4f));
            }

            var labelled = new HashSet<int>();
            MasterState s = MastermindNet.State;
            foreach (var landing in landings)
            {
                if (!visible || !Any(landing.Value) || landing.Key >= beaches.Count) continue;
                Vector3 point = camera.cameraRef.WorldToScreenPoint(beaches[landing.Key].center);
                if (!OnScreen(point)) continue;
                labelled.Add(landing.Key);
                if (!landingLabels.TryGetValue(landing.Key, out Text label) || !label)
                {
                    label = NativeWidgets.Text(battleRoot.transform, font, "", 20);
                    label.transform.SetAsFirstSibling();
                    landingLabels[landing.Key] = label;
                }
                label.text = $"{Crew(landing.Value, s)} Vikings  ·  <color=#FFD36B>{Cost(landing.Value)}</color> gold";
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, point, null, out Vector2 at);
                var rect = label.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = at + new Vector2(0f, landing.Key == beachIndex ? 66f : 40f);
                if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
            }
            foreach (var label in landingLabels)
                if (!labelled.Contains(label.Key) && label.Value && label.Value.gameObject.activeSelf) label.Value.gameObject.SetActive(false);
        }

        private void RefreshCommanderButtons(MasterState s, bool live)
        {
            var state = s.upgrades;
            int count = state != null ? state.commanders.Count : 0;
            int deployed = 0;
            for (int i = 0; i < count; i++) if (state.commanders[i].status == CommanderStatus.Deployed) deployed++;
            commanderRow.gameObject.SetActive(count > 0);
            for (int i = 0; i < commanderButtons.Count; i++)
            {
                NativeRoundButton button = commanderButtons[i];
                button.SetActive(i < count);
                if (i >= count) continue;
                var commander = state.commanders[i];
                VikingReference reference = Reference(commander.type);
                button.SetIcon(UnitIcons.Of(reference));
                button.SetCount(commander.id.ToString());
                bool picked = selected.Contains(commander.id);
                button.Selected = picked;
                bool rests = commander.status == CommanderStatus.Resting;
                if (commanderRests[i].activeSelf != rests) commanderRests[i].SetActive(rests);
                bool dead = commander.status == CommanderStatus.Deployed && Fallen(commander.id);
                if (commanderSkulls[i].activeSelf != dead) commanderSkulls[i].SetActive(dead);
                button.SetTooltip(CommanderCount(commander));
                switch (commander.status)
                {
                    case CommanderStatus.Deployed:
                        bool fallen = Fallen(commander.id);
                        button.Available = !fallen;
                        button.caption.text = fallen ? "" : picked ? "Ordering" : "On island"; // a skull marks the fallen
                        break;
                    case CommanderStatus.Resting:
                        button.Available = false;
                        button.caption.text = ""; // the hourglass says it
                        break;
                    default:
                        bool room = deployed < EconomyConfig.MaxCommandersDeployed;
                        button.Available = live && room;
                        button.caption.text = room ? "Deploy" : "Island full";
                        break;
                }
            }
            selected.RemoveAll(id => state == null || !state.commanders.Exists(c => c.id == id && c.status == CommanderStatus.Deployed));
            string ordering = selected.Count == 1 ? $"Commander {selected[0]}" : $"{selected.Count} commanders";
            hintText.text = Ordering
                ? $"{ordering}:  click a tile to move  ·  a house to burn  ·  R lets them roam  ·  right-click when done"
                : deployed > 1 ? "Shift-click or Alt+drag to order several commanders at once" : "";
        }

        // Commanders whose Vikings have been seen on this island (their mirrored Vikings show up a moment after landing).
        private readonly HashSet<int> seenAlive = new HashSet<int>();

        /// <summary>
        /// A commander's tooltip, just how many Vikings it has: "9 Swordsmen"; on the island, how many are left
        /// ("6 Swordsmen left", "None left"). Its trait shows on its flag, its state on its button.
        /// </summary>
        public string CommanderCount(MasterUpgradeState.CommanderInfo commander)
        {
            if (commander.status == CommanderStatus.Deployed && builtFor)
            {
                if (Fallen(commander.id)) return "None left";
                int alive = PuppetWorld.CommanderAlive(commander.id);
                if (alive > 0) return VikingCommanders.Count(alive, commander.type) + " left";
            }
            return VikingCommanders.Count(commander.crew, commander.type);
        }

        /// <summary>For the war chest: has this commander's squad fallen on the island being fought?</summary>
        public bool IsFallen(int id) => builtFor && Fallen(id);

        /// <summary>Landed this island and seen alive, but none of its Vikings are left.</summary>
        private bool Fallen(int id)
        {
            if (CommanderPosition(id, out _)) { seenAlive.Add(id); return false; }
            return seenAlive.Contains(id) && CommanderKnown(id);
        }

        private void CommanderClicked(int slot)
        {
            var state = MastermindNet.State.upgrades;
            if (state == null || slot >= state.commanders.Count) return;
            var commander = state.commanders[slot];
            if (commander.status == CommanderStatus.Deployed && Fallen(commander.id)) FabricWrapper.PostEvent("UI/InGame/Error");
            else if (commander.status == CommanderStatus.Deployed) Select(commander.id, Shift);
            else if (commander.status == CommanderStatus.Resting) FabricWrapper.PostEvent("UI/InGame/Error");
            else if (beachIndex < 0)
            {
                FabricWrapper.PostEvent("UI/InGame/Error");
                GameNotify.Post("Click a glowing shore tile first: the commander lands there", 2.5f);
            }
            else
            {
                MastermindNet.RequestCommanderDeploy(commander.id, beachIndex);
                FabricWrapper.PostEvent("UI/InGame/SquadSpawn");
            }
        }

        /// <summary>Left-click adds to the selected landing, right-click removes (Shift: five at a time).</summary>
        private void CardClicked(UnitCard card, PointerEventData.InputButton button)
        {
            if (beachIndex < 0)
            {
                FabricWrapper.PostEvent("UI/InGame/Error");
                GameNotify.Post("Click a glowing shore tile first: that's where these Vikings land", 2.5f);
                return;
            }
            if (!landings.TryGetValue(beachIndex, out int[] counts)) landings[beachIndex] = counts = new int[cards.Count];
            int index = cards.IndexOf(card);
            int step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 5 : 1;
            int before = counts[index];
            if (button == PointerEventData.InputButton.Left)
            {
                // Only what this wave's gold still pays for: the leader refuses a whole plan that costs more, and the
                // wave would sail without it (the button only looked greyed out, it still added).
                MasterState s = MastermindNet.State;
                if (MastermindNet.HasFreshState && s.inBattle)
                {
                    int unit = Economy.UnitCost(card.type);
                    float left = s.waveBudget - Cost();
                    int fits = unit > 0 ? Mathf.FloorToInt((left + 0.001f) / unit) : step;
                    if (fits <= 0)
                    {
                        FabricWrapper.PostEvent("UI/InGame/Error");
                        GameNotify.Post($"Not enough gold left in this wave: {Name((int)card.type.type)} costs {unit}, {Mathf.Max(0, Mathf.FloorToInt(left + 0.001f))} left", 2.5f);
                        return;
                    }
                    step = Mathf.Min(step, fits);
                }
                counts[index] = Mathf.Min(99, before + step);
            }
            else if (button == PointerEventData.InputButton.Right) counts[index] = Mathf.Max(0, before - step);
            if (counts[index] == before) return;
            FabricWrapper.PostEvent(counts[index] > before ? "UI/InGame/TapRight" : "UI/InGame/TapLeft");
            PlanChanged();
        }

        /// <summary>The plan as the leader takes it: per planned beach, the paid Vikings per type.</summary>
        private List<KeyValuePair<int, List<KeyValuePair<VikingAgent.Type, int>>>> PlannedLandings()
        {
            var plan = new List<KeyValuePair<int, List<KeyValuePair<VikingAgent.Type, int>>>>();
            foreach (var landing in landings)
            {
                var units = new List<KeyValuePair<VikingAgent.Type, int>>();
                for (int i = 0; i < landing.Value.Length && i < cards.Count; i++)
                    if (landing.Value[i] > 0) units.Add(new KeyValuePair<VikingAgent.Type, int>(cards[i].type.type, landing.Value[i]));
                if (units.Count > 0) plan.Add(new KeyValuePair<int, List<KeyValuePair<VikingAgent.Type, int>>>(landing.Key, units));
            }
            return plan;
        }

        // The leader keeps the current plan and sails it when the wave's timer runs out. A plan made before the
        // fight starts (or while it's paused) waits here, since the leader only accepts plans during the fight.
        private void PlanChanged() { planDirty = true; planChangedAt = Time.unscaledTime; }

        private void SyncPlan()
        {
            if (!planDirty || Time.unscaledTime - planChangedAt < 0.3f || !MastermindNet.HasFreshState || !MastermindNet.State.inBattle) return;
            planDirty = false;
            MastermindNet.RequestWavePlan(PlannedLandings());
        }

        // ---- World markers ----------------------------------------------------------------------

        /// <summary>A small copy of the commander's button floats over its squad; click it to select.</summary>
        private void RefreshCommanderMarkers()
        {
            var camera = Singleton<LevelCamera>.instance;
            var state = MastermindNet.State.upgrades;
            if (!camera || !camera.cameraRef || state == null) return;
            var shown = new HashSet<int>();
            foreach (var commander in state.commanders)
            {
                if (commander.status != CommanderStatus.Deployed || !CommanderPosition(commander.id, out Vector3 position)) continue;
                shown.Add(commander.id);
                if (!commanderMarkers.TryGetValue(commander.id, out NativeRoundButton marker))
                {
                    int id = commander.id;
                    marker = NativeRoundButton.Create(battleRoot.transform, 46f, font, button =>
                    {
                        if (button == PointerEventData.InputButton.Left) Select(id, Shift);
                        else if (button == PointerEventData.InputButton.Right && Ordering) Deselect();
                    });
                    marker.root.transform.SetAsFirstSibling(); // the HUD stays above world markers
                    var rect = (RectTransform)marker.root.transform;
                    rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                    VikingReference reference = Reference(commander.type);
                    marker.SetIcon(UnitIcons.Of(reference));
                    marker.SetCount(id.ToString());
                    commanderMarkers[id] = marker;
                }
                marker.SetTooltip(CommanderCount(commander));
                Vector3 screen = camera.cameraRef.WorldToScreenPoint(position + Vector3.up * 0.7f);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
                ((RectTransform)marker.root.transform).anchoredPosition = local;
                marker.Selected = selected.Contains(commander.id);
                marker.SetActive(OnScreen(screen));
            }
            var gone = new List<int>();
            foreach (int id in commanderMarkers.Keys) if (!shown.Contains(id)) gone.Add(id);
            foreach (int id in gone) { Destroy(commanderMarkers[id].root); commanderMarkers.Remove(id); }
        }

        private static bool OnScreen(Vector3 screen) =>
            screen.z > 0f && screen.x >= 0f && screen.x <= Screen.width && screen.y >= 0f && screen.y <= Screen.height;

        private void ClearWorld()
        {
            foreach (var spot in landTiles) if (spot) spot.RemoveHighlight(landTint);
            landTiles.Clear();
            if (chosenSpot) chosenSpot.RemoveHighlight(chosenTint);
            if (hoveredSpot) hoveredSpot.RemoveHighlight(hoverTint);
            chosenSpot = hoveredSpot = null;
            foreach (var marker in commanderMarkers.Values) if (marker.root) Destroy(marker.root);
            commanderMarkers.Clear();
            foreach (var spot in orderedTiles.Values) if (spot) spot.RemoveHighlight(orderTint);
            orderedTiles.Clear();
            if (landPin) landPin.gameObject.SetActive(false);
            foreach (var spot in plannedSpots.Values) if (spot) spot.RemoveHighlight(plannedTint);
            plannedSpots.Clear();
            foreach (var label in landingLabels.Values) if (label) Destroy(label.gameObject);
            landingLabels.Clear();
            landings.Clear();
            selected.Clear();
            seenAlive.Clear();
            beachIndex = shownWave = -1;
            marqueeDragging = false;
            if (marqueeBox) marqueeBox.gameObject.SetActive(false);
            leftPressed = false;
        }

        // ---- Building ---------------------------------------------------------------------------

        private void BuildCanvas()
        {
            FindFont();
            canvasObject = new GameObject("BNMP Mastermind Canvas");
            DontDestroyOnLoad(canvasObject);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            // The game's masked sprites read extra vertex channels.
            canvas.additionalShaderChannels = NativeWidgets.ShaderChannels;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            raycaster = canvasObject.AddComponent<GraphicRaycaster>();
            canvasRect = (RectTransform)canvasObject.transform;

            battleRoot = Node(canvasObject.transform, "Battle");
            Stretch(battleRoot);
            mapRoot = Node(canvasObject.transform, "Map");
            Stretch(mapRoot);
        }

        private void BuildBattle()
        {
            ClearWorld();
            foreach (Transform child in battleRoot.transform) Destroy(child.gameObject);
            cards.Clear();
            commanderButtons.Clear();
            commanderRests.Clear();
            commanderSkulls.Clear();
            commanderMarkers.Clear();
            builtFor = null; // rebuild the army row too
            battleBuilt = true;
            battleNative = NativeWidgets.HasRoundButton;
            if (!battleNative) ModLog.Warn("Mastermind HUD: the game's ability button isn't loaded yet; using plain buttons for now");

            BuildLandPin();
            BuildMarquee();

            // Top centre: the wave, in the game's white over-the-island lettering.
            waveText = NativeWidgets.Text(battleRoot.transform, font, "", 36);
            Place(waveText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f));
            waveSub = NativeWidgets.Text(battleRoot.transform, font, "", 22);
            Place(waveSub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f));

            // Top right: the war chest coin (opens the war chest).
            chestAmount = ChestButton(battleRoot.transform, new Vector2(1f, 1f), new Vector2(-40f, -34f), () => WarChestPopup.Open(battle: true), out RectTransform chest);
            chestGain = NativeWidgets.Text(chest, font, "", 24);
            chestGain.fontStyle = FontStyle.Bold;
            chestGain.GetComponent<LayoutElement>().ignoreLayout = true;
            chestGainRect = chestGain.rectTransform;
            chestGainRect.anchorMin = chestGainRect.anchorMax = new Vector2(0.5f, 0f);
            chestGainRect.pivot = new Vector2(0.5f, 1f);
            chestGain.gameObject.SetActive(false);

            // Bottom centre: one round button per Viking type (when the wave sails is in the text at the top).
            armyRow = Row(battleRoot.transform, "Army", new Vector2(0.5f, 0f), new Vector2(0f, 46f), 18f);
            costText = NativeWidgets.Text(battleRoot.transform, font, "", 28);
            Place(costText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 180f));
            statusText = NativeWidgets.Text(battleRoot.transform, font, "", 21);
            Place(statusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 220f));

            // Bottom left: commanders, and what to do while one takes orders.
            commanderRow = Row(battleRoot.transform, "Commanders", new Vector2(0f, 0f), new Vector2(40f, 46f), 16f);
            for (int i = 0; i < EconomyConfig.MaxCommanders; i++)
            {
                int slot = i;
                var button = NativeRoundButton.Create(commanderRow, 72f, font, click =>
                {
                    if (click == PointerEventData.InputButton.Left) CommanderClicked(slot);
                    else if (click == PointerEventData.InputButton.Right && Ordering) Deselect();
                });
                button.SetActive(false);
                commanderButtons.Add(button);
                commanderRests.Add(ModIcons.Badge(button.root.transform, ModIcons.Hourglass, 32f));
                commanderSkulls.Add(ModIcons.Badge(button.root.transform, ModIcons.Skull, 32f));
            }
            hintText = NativeWidgets.Text(battleRoot.transform, font, "", 21, TextAnchor.MiddleLeft);
            Place(hintText.rectTransform, new Vector2(0f, 0f), new Vector2(40f, 170f));
        }

        private void BuildMap()
        {
            mapBuilt = true;
            // Campaign map: the same coin opens the war chest (the map's Upgrades button does too).
            mapChestAmount = ChestButton(mapRoot.transform, new Vector2(1f, 0f), new Vector2(-48f, 130f), () => WarChestPopup.Open(battle: false), out _);
        }

        /// <summary>The coin with the war chest's gold, captioned; clicking it opens the war chest.</summary>
        private Text ChestButton(Transform parent, Vector2 anchor, Vector2 position, Action open, out RectTransform root)
        {
            GameObject holder = Node(parent, "War chest");
            root = (RectTransform)holder.transform;
            root.anchorMin = root.anchorMax = root.pivot = anchor;
            root.anchoredPosition = position;
            var hit = holder.AddComponent<Image>();
            hit.color = Color.clear;
            var layout = holder.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fitter = holder.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            NativeWidgets.Coin(holder.transform, 46f);
            Text amount = NativeWidgets.Text(holder.transform, font, "-", 34);
            Text caption = NativeWidgets.Text(holder.transform, font, "War chest", 17);
            caption.GetComponent<LayoutElement>().ignoreLayout = true;
            var captionRect = caption.rectTransform;
            captionRect.anchorMin = captionRect.anchorMax = new Vector2(0.5f, 0f);
            captionRect.pivot = new Vector2(0.5f, 1f);
            captionRect.anchoredPosition = new Vector2(0f, 2f);
            var relay = holder.AddComponent<ClickRelay>();
            relay.onClick = button =>
            {
                if (button != PointerEventData.InputButton.Left) return;
                FabricWrapper.PostEvent(FabricID.uiButtonClick);
                open();
            };
            relay.onHover = hovering =>
            {
                amount.color = hovering ? Gold : Color.white;
                holder.transform.localScale = Vector3.one * (hovering ? 1.06f : 1f);
                if (hovering) FabricWrapper.PostEvent(FabricID.uiHover);
            };
            return amount;
        }

        /// <summary>The Alt+drag selection box and the invisible catcher that takes the drag from the camera.</summary>
        private void BuildMarquee()
        {
            marqueeCatcher = Node(battleRoot.transform, "Box select");
            Stretch(marqueeCatcher);
            marqueeCatcher.AddComponent<Image>().color = Color.clear;
            marqueeCatcher.AddComponent<MarqueeInput>().owner = this;
            marqueeCatcher.transform.SetAsFirstSibling(); // under the HUD, over the island
            marqueeCatcher.SetActive(false);
            GameObject box = Node(battleRoot.transform, "Box");
            marqueeBox = (RectTransform)box.transform;
            marqueeBox.anchorMin = marqueeBox.anchorMax = new Vector2(0.5f, 0.5f);
            marqueeBox.pivot = Vector2.zero;
            var fill = box.AddComponent<Image>();
            fill.color = new Color(1f, 1f, 1f, 0.12f);
            fill.raycastTarget = false;
            var outline = box.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);
            box.SetActive(false);
        }

        private void BuildLandPin()
        {
            // A gold diamond with a dark rim, like a map pin, over the picked landing tile.
            GameObject pin = Node(battleRoot.transform, "Landing pin");
            landPin = (RectTransform)pin.transform;
            landPin.anchorMin = landPin.anchorMax = landPin.pivot = new Vector2(0.5f, 0.5f);
            landPin.sizeDelta = new Vector2(24f, 24f);
            landPin.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var rim = pin.AddComponent<Image>();
            rim.color = new Color(0.15f, 0.12f, 0.1f, 0.9f);
            rim.raycastTarget = false;
            GameObject core = Node(pin.transform, "Core");
            Stretch(core);
            ((RectTransform)core.transform).offsetMin = new Vector2(4f, 4f);
            ((RectTransform)core.transform).offsetMax = new Vector2(-4f, -4f);
            var fill = core.AddComponent<Image>();
            fill.color = new Color(1f, 0.78f, 0.25f, 1f);
            fill.raycastTarget = false;
            pin.SetActive(false);
        }

        private void BuildArmy(Island island)
        {
            foreach (UnitCard card in cards) if (card.button.root) Destroy(card.button.root);
            cards.Clear();
            landings.Clear();
            int index = 0;
            foreach (VikingReference type in Economy.AvailableTypes(island))
            {
                var card = new UnitCard { type = type };
                card.button = NativeRoundButton.Create(armyRow, 80f, font, click => CardClicked(card, click));
                card.button.root.transform.SetSiblingIndex(index++); // Send now stays last
                card.button.SetIcon(UnitIcons.Of(type));
                card.button.SetTooltip(Name((int)type.type));
                card.button.caption.text = $"<color=#FFD36B>{Economy.UnitCost(type)}</color> gold";
                cards.Add(card);
            }
        }

        // ---- Layout helpers ---------------------------------------------------------------------

        private static RectTransform Row(Transform parent, string name, Vector2 anchor, Vector2 position, float spacing)
        {
            GameObject row = Node(parent, name);
            var rect = (RectTransform)row.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.LowerCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fitter = row.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(anchor.x, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(10f, 30f);
        }

        private static GameObject Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(GameObject go)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private void FindFont()
        {
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            foreach (MainMenu menu in Resources.FindObjectsOfTypeAll<MainMenu>())
            {
                var prefab = Traverse.Create(menu).Field<ButtonWidget>("textButtonPrefab").Value;
                if (prefab && prefab.label && prefab.label.font) { font = prefab.label.font; break; }
            }
        }
    }

    /// <summary>Alt+drag over the island: forwards the drag to the panel's box select.</summary>
    internal sealed class MarqueeInput : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public MastermindPanel owner;
        public void OnPointerDown(PointerEventData eventData) { if (eventData.button == PointerEventData.InputButton.Left) owner.MarqueeDown(eventData.position); }
        public void OnDrag(PointerEventData eventData) => owner.MarqueeDrag(eventData.position);
        public void OnPointerUp(PointerEventData eventData) => owner.MarqueeUp(eventData.position);
    }

    /// <summary>Left and right clicks, and hover, for a UI element.</summary>
    internal sealed class ClickRelay : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Action<PointerEventData.InputButton> onClick;
        public Action<bool> onHover;
        public void OnPointerClick(PointerEventData eventData) => onClick?.Invoke(eventData.button);
        public void OnPointerEnter(PointerEventData eventData) => onHover?.Invoke(true);
        public void OnPointerExit(PointerEventData eventData) => onHover?.Invoke(false);
        private void OnDisable() => onHover?.Invoke(false);
    }
}
