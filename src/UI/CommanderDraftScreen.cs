using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BadNorthMP.Net;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense;
using Voxels.TowerDefense.HeroGeneration;

namespace BadNorthMP.UI
{
    /// <summary>
    /// The multiplayer commander draft, laid out as the game's New Game screen: every defender's commander cards (your
    /// own first, with arrows) either side of the game's own settings panel, which shows the mastermind's difficulty and
    /// restarts and holds Ready (Back for the mastermind).
    /// </summary>
    internal sealed class CommanderDraftScreen : MonoBehaviour
    {
        private const float CardGap = 28f, MaxRowWidth = 1840f;
        private static readonly Color Light = new Color(0.86f, 0.84f, 0.8f), Soft = new Color(0.62f, 0.6f, 0.57f);
        private static CommanderDraftScreen instance;
        private GameObject canvasObject;
        private RectTransform row;
        private Text title, roster, hint;

        // The settings panel in the middle: the game's own (NativeNewGamePanel), or a plain one if it can't be copied.
        private GameObject center;
        private float centerWidth;
        private Action<int, bool> showSettings;
        private Action<string, bool, Action> setButton;

        // One card per commander slot shown, in order, each in its column under its player's name. A new portrait
        // rebuilds only that card (the old one stays up until the new one is ready); a new trait or item is shown in place.
        private sealed class Slot
        {
            public ulong player;
            public int index, choice;
            public string trait, item;
            public HeroDefinition hero;
            public NativeCommanderCard card;
            public GameObject column;
            public Text label;
        }
        private readonly List<Slot> rendered = new List<Slot>();
        private readonly List<Slot> building = new List<Slot>();
        private string renderedLayout;
        private bool baking;
        private string previewError;
        private Coroutine generation;

        private void Awake() => instance = this;
        public static void Open()
        {
            if (instance && PartyFlow.State.phase == 1) instance.Show();
        }
        private void Update()
        {
            bool show = PartyFlow.Active && PartyFlow.State.phase == 1 && Updater.Compatible;
            if (!show)
            {
                if (canvasObject && canvasObject.activeSelf) { canvasObject.SetActive(false); ClearPreviews(); }
                return;
            }
            if (!canvasObject || !canvasObject.activeSelf) Show();
            var seat = PartyFlow.Mine;
            // Continuing a save: its commanders are the ones that play, so there's nothing to pick, only Ready.
            bool continuing = PartyFlow.State.Continuing;
            List<Slot> wanted = continuing ? new List<Slot>() : Wanted();
            if (continuing && rendered.Count + building.Count > 0) ClearPreviews();
            title.text = continuing ? "Continuing Campaign" : "New Campaign";
            EnsureCenter(CardWidth(continuing ? 2 : wanted.Count));
            if (wanted.Count > 0)
            {
                string layout = Layout(wanted);
                if (layout != renderedLayout)
                {
                    ClearPreviews();
                    renderedLayout = layout;
                    generation = StartCoroutine(Generate(wanted, null));
                }
                else if (!baking && previewError == null) Sync(wanted);
            }

            showSettings(PartyFlow.State.difficulty, PartyFlow.State.allowReplays);
            if (seat != null)
                setButton(previewError != null && !continuing ? "Retry cards" : PartyFlow.PendingPick ? "Confirming..." : seat.ready ? "Unready" : "Ready",
                    !PartyFlow.PendingPick && (continuing || (!baking && (previewError != null || rendered.Count == wanted.Count))), Confirm);
            else if (SteamSession.Instance.IsHost) setButton("Back", true, PartyFlow.CancelDraft);
            else setButton("", false, null);

            foreach (Slot slot in rendered) if (slot.label) slot.label.text = PlayerLine(slot.player, continuing);
            var line = new StringBuilder(PartyFlow.Status);
            if (continuing)
            {
                line.Append('\n');
                for (int i = 0; i < PartyFlow.State.seats.Count; i++)
                    line.Append(i > 0 ? "      " : "").Append(PlayerLine(PartyFlow.State.seats[i].player, true));
            }
            roster.text = line.ToString();
            hint.text = continuing
                ? (seat == null ? "Waiting for the defenders to press Ready."
                    : "Your commanders come from the save, with the looks, traits, items and upgrades they already have. Players who led " +
                      "commanders in this campaign before keep them; commanders whose players aren't here are shared out among those who are.")
                : previewError ?? (seat == null ? "Defenders choose their portraits, starting traits and items, then press Ready."
                    : "Choose your portrait, starting trait and item with the arrows. Your squad starts as militia: classes are bought during the campaign.");
        }

        private static string PlayerLine(ulong player, bool continuing)
        {
            DefenderSeat seat = PartyFlow.State.Find(player);
            string state = seat == null ? "" : seat.ready ? "Ready" : continuing ? "Not ready" : "Choosing";
            string name = player == SteamSession.Instance.Me.m_SteamID ? "You" : Plain(SteamSession.NameOf(new CSteamID(player)));
            return state.Length == 0 ? name : name + "  ·  " + state;
        }

        /// <summary>Every seat's commander slots with their current picks: yours first, then your teammates' in seat order.</summary>
        private static List<Slot> Wanted()
        {
            var wanted = new List<Slot>();
            ulong me = SteamSession.Instance.Me.m_SteamID;
            foreach (bool mine in new[] { true, false })
                foreach (var seat in PartyFlow.State.seats)
                {
                    if ((seat.player == me) != mine) continue;
                    for (int i = 0; i < seat.choices.Length; i++)
                        wanted.Add(new Slot { player = seat.player, index = i, choice = seat.choices[i], trait = seat.traits[i], item = seat.items[i] });
                }
            return wanted;
        }

        /// <summary>Which slots are shown (not what's picked in them): when this changes, every card is rebuilt.</summary>
        private static string Layout(List<Slot> wanted)
        {
            var key = new StringBuilder().Append(PartyFlow.State.token).Append('|');
            foreach (Slot slot in wanted) key.Append(slot.player).Append(':').Append(slot.index).Append(';');
            return key.ToString();
        }

        /// <summary>Cards shrink as there are more of them (up to four, either side of the settings).</summary>
        private static float CardWidth(int cards) => cards <= 2 ? 300f : cards == 3 ? 260f : 230f;

        private void Sync(List<Slot> wanted)
        {
            var rebuild = new List<int>();
            for (int i = 0; i < wanted.Count && i < rendered.Count; i++)
            {
                Slot have = rendered[i], want = wanted[i];
                if (have.choice != want.choice) { rebuild.Add(i); continue; }
                if (have.trait == want.trait && have.item == want.item) continue;
                try
                {
                    PartyLoadout.PreparePreview(have.hero, want.trait, want.item);
                    have.card.SetUpgrades(have.hero);
                    have.trait = want.trait;
                    have.item = want.item;
                }
                catch (Exception e) { PreviewFailed(e); return; }
            }
            if (rebuild.Count > 0) generation = StartCoroutine(Generate(wanted, rebuild));
        }
        private void Show()
        {
            MultiplayerScreen.CloseForBattle();
            if (!canvasObject) Build();
            canvasObject.SetActive(true);
        }
        /// <summary>Build the cards for <paramref name="only"/> (indices into <paramref name="wanted"/>), or all of them.</summary>
        private IEnumerator Generate(List<Slot> wanted, List<int> only)
        {
            baking = true; previewError = null;
            yield return null; // Release old cards and their atlases before allocating replacements.
            var indices = new List<int>();
            if (only != null) indices.AddRange(only);
            else for (int i = 0; i < wanted.Count; i++) indices.Add(i);
            foreach (int i in indices)
            {
                Slot slot = wanted[i];
                try { slot.hero = PartyFlow.CreateStartingHero(PartyFlow.StartingHeroId(slot.player, slot.index), slot.choice, slot.trait, slot.item); }
                catch (Exception e) { PreviewFailed(e); }
                if (slot.hero == null) { ReleaseBuilding(); baking = false; generation = null; yield break; }
                LogCard(slot);
                building.Add(slot);
                yield return null;
            }
            var monos = new List<MonoHero>(); foreach (Slot slot in building) monos.Add(slot.hero.monoHero);
            var bake = MonoHero.BakeSprites(monos).GetEnumerator();
            while (true)
            {
                bool more = false;
                try { more = bake.MoveNext(); } catch (Exception e) { PreviewFailed(e); }
                if (previewError != null) { ReleaseBuilding(); baking = false; generation = null; yield break; }
                if (!more) break;
                yield return null;
            }
            float width = CardWidth(wanted.Count);
            foreach (int i in indices)
            {
                Slot slot = wanted[i];
                int index = slot.index;
                bool own = slot.player == SteamSession.Instance.Me.m_SteamID;
                Slot old = only != null ? rendered[i] : null;
                if (old != null) { slot.column = old.column; slot.label = old.label; }
                else slot.column = Column(out slot.label);
                try
                {
                    slot.card = NativeCommanderCard.Create(slot.column.transform, slot.hero,
                        own ? (Action<int>)(step => PartyFlow.ChooseCommander(index, (PartyFlow.Mine.choices[index] + step + PartyRoster.ChoiceCount) % PartyRoster.ChoiceCount)) : null,
                        own ? (Action<int>)(step => PartyFlow.CycleUpgrade(index, true, step)) : null,
                        own ? (Action<int>)(step => PartyFlow.CycleUpgrade(index, false, step)) : null,
                        width);
                    if (slot.card == null) throw new Exception("The native commander card could not be loaded.");
                }
                catch (Exception e) { PreviewFailed(e); }
                building.Remove(slot);
                if (slot.card == null)
                {
                    Release(slot);
                    if (old == null && slot.column) Destroy(slot.column);
                    continue;
                }
                if (old == null) { rendered.Add(slot); continue; }
                // The new card takes the old one's place in its column.
                slot.card.root.transform.SetSiblingIndex(old.card.root.transform.GetSiblingIndex());
                old.column = null;
                Release(old);
                rendered[i] = slot;
            }
            SpaceCards(width);
            Arrange();
            baking = false; generation = null;
        }

        /// <summary>
        /// Room for the arrow tabs: a card under the mouse slides them out past its sides, over its neighbours unless each
        /// card has that much room. The whole row shrinks if it would be wider than the screen.
        /// </summary>
        private void SpaceCards(float cardWidth)
        {
            float reach = 0f;
            foreach (Slot slot in rendered) if (slot.card != null) reach = Mathf.Max(reach, slot.card.TabReach);
            foreach (Slot slot in rendered)
                if (slot.card != null && slot.card.root) slot.card.root.GetComponent<LayoutElement>().preferredWidth = cardWidth + 2f * reach;
            float needed = rendered.Count * (cardWidth + 2f * reach + CardGap) + centerWidth;
            row.localScale = Vector3.one * Mathf.Min(1f, MaxRowWidth / Mathf.Max(1f, needed));
            ModLog.Info($"Commander cards: {rendered.Count} at {cardWidth:0} wide, arrow tabs reach {reach:0} past each side");
        }

        /// <summary>The first half of the cards (yours first) left of the settings, the rest right of them.</summary>
        private void Arrange()
        {
            int left = (rendered.Count + 1) / 2, at = 0;
            for (int i = 0; i < rendered.Count; i++)
            {
                if (i == left && center) center.transform.SetSiblingIndex(at++);
                if (rendered[i].column) rendered[i].column.transform.SetSiblingIndex(at++);
            }
            if (rendered.Count <= left && center) center.transform.SetSiblingIndex(at);
        }

        /// <summary>The settings panel, sized like cards <paramref name="cardWidth"/> wide (made again when that changes).</summary>
        private void EnsureCenter(float cardWidth)
        {
            if (center && Mathf.Approximately(cardWidth, centerWidthFor)) return;
            if (center) Destroy(center);
            centerWidthFor = cardWidth;
            var native = NativeNewGamePanel.Create(row, NativeCommanderCard.ScaleFor(cardWidth));
            if (native != null)
            {
                center = native.root;
                showSettings = native.Show;
                setButton = native.SetButton;
            }
            else BuildPlainCenter();
            centerWidth = LayoutUtility.GetPreferredWidth((RectTransform)center.transform);
            Arrange();
        }
        private float centerWidthFor = -1f;

        /// <summary>The settings in the mod's own style, if the game's panel can't be copied.</summary>
        private void BuildPlainCenter()
        {
            var panel = GameStyle.Panel(row, new RectOffset(28, 28, 22, 22), 14);
            Text difficulty = GameStyle.Label(panel, "", 32), restarts = GameStyle.Label(panel, "", 24);
            Action pressed = null;
            Button button = GameStyle.Button(panel, "Ready", () => { FabricWrapper.PostEvent(FabricID.uiButtonClick); pressed?.Invoke(); }, 240f, 58f);
            Text label = button.GetComponentInChildren<Text>();
            center = panel.gameObject;
            showSettings = (level, replays) =>
            {
                difficulty.text = NativeNewGamePanel.DifficultyName(level);
                restarts.text = replays ? "Level restarts allowed" : "No level restarts";
            };
            setButton = (text, interactable, action) =>
            {
                pressed = action;
                label.text = text;
                button.interactable = interactable;
                if (button.gameObject.activeSelf != (action != null)) button.gameObject.SetActive(action != null);
            };
        }

        /// <summary>
        /// What a card was made from, and a hash of the face it got: two players' logs show whether their games drew
        /// the same commander for the same pick.
        /// </summary>
        private static void LogCard(Slot slot)
        {
            string look = slot.hero.propertyBank != null ? slot.hero.propertyBank.GetPrint() : "";
            uint hash = 2166136261;
            foreach (char c in look) unchecked { hash ^= c; hash *= 16777619; }
            ModLog.Info($"Draft card: {SteamSession.NameOf(new CSteamID(slot.player))} slot {slot.index + 1}: commander {PartyFlow.StartingHeroId(slot.player, slot.index)}, " +
                $"choice {slot.choice + 1}, trait '{slot.trait}', item '{slot.item}', party {PartyFlow.State.seed} -> {slot.hero.dbgName}, look {hash:x8}");
        }

        private void ReleaseBuilding()
        {
            foreach (Slot slot in building) Release(slot);
            building.Clear();
        }

        private void Release(Slot slot)
        {
            if (slot.card != null && slot.card.root) Destroy(slot.card.root);
            slot.card = null;
            if (slot.hero != null && slot.hero.monoHero) { slot.hero.monoHero.Destroy(); Destroy(slot.hero.monoHero.gameObject); }
        }
        private void PreviewFailed(Exception error)
        {
            previewError = "Commander preview failed. Retry cards; details are in BepInEx/LogOutput.log.";
            ModLog.Warn("Commander preview failed: " + error);
        }
        private void Confirm()
        {
            if (previewError != null) { previewError = null; ClearPreviews(); return; }
            PartyFlow.Ready();
        }
        private void ClearPreviews()
        {
            if (generation != null) { StopCoroutine(generation); generation = null; }
            ReleaseBuilding();
            foreach (Slot slot in rendered)
            {
                Release(slot);
                if (slot.column) Destroy(slot.column);
            }
            rendered.Clear();
            baking = false; renderedLayout = null;
        }
        private void OnDestroy() { ClearPreviews(); if (canvasObject) Destroy(canvasObject); }

        private void Build()
        {
            canvasObject = GameStyle.Canvas("BNMP Commander Selection", 650);
            var backdrop = Node(canvasObject.transform, "Backdrop");
            GameStyle.Stretch((RectTransform)backdrop.transform);
            backdrop.AddComponent<Image>().color = new Color(0.21f, 0.2f, 0.22f, 0.97f);

            title = MakeText(canvasObject.transform, "New Campaign", 38, Light);
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1400f, 60f));
            Place(Dashes(canvasObject.transform), new Vector2(0.5f, 1f), new Vector2(0f, -215f), new Vector2(1500f, 2f));
            roster = MakeText(canvasObject.transform, "", 22, Soft);
            Place(roster.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -232f), new Vector2(1500f, 64f));
            roster.alignment = TextAnchor.UpperCenter;

            var rowObject = Node(canvasObject.transform, "Commanders and settings");
            row = (RectTransform)rowObject.transform;
            Place(row, new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), Vector2.zero);
            var layout = rowObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = CardGap; layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fit = rowObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            hint = MakeText(canvasObject.transform, "", 22, Soft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            hint.alignment = TextAnchor.UpperCenter;
            Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1300f, 70f));
            var leave = GameStyle.Button(canvasObject.transform, "Leave lobby", () => { FabricWrapper.PostEvent(FabricID.uiButtonClick); SteamSession.Instance.Leave(); }, 220f, 50f);
            Place((RectTransform)leave.transform, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(220f, 50f));
        }

        /// <summary>A card's column: its player and whether they're ready, over the card.</summary>
        private GameObject Column(out Text label)
        {
            var column = Node(row, "Commander");
            var layout = column.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10; layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            label = MakeText(column.transform, "", 24, Light);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            return column;
        }

        private static GameObject Node(Transform parent, string name)
        { var node = new GameObject(name, typeof(RectTransform)); node.transform.SetParent(parent, false); return node; }

        private static Text MakeText(Transform parent, string value, int size, Color color)
        {
            var text = Node(parent, "Text").AddComponent<Text>();
            text.font = GameStyle.Font; text.fontSize = size; text.color = color; text.text = value;
            text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, anchor.y >= 1f ? 1f : anchor.y <= 0f ? 0f : 0.5f);
            rect.anchoredPosition = position;
            if (size != Vector2.zero) rect.sizeDelta = size;
        }

        private static Texture2D dash;

        /// <summary>The dashed rule under the title, as on the game's own New Game screen.</summary>
        private static RectTransform Dashes(Transform parent)
        {
            if (!dash)
            {
                dash = new Texture2D(10, 1) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
                for (int x = 0; x < 10; x++) dash.SetPixel(x, 0, x < 5 ? Color.white : Color.clear);
                dash.Apply();
            }
            var line = Node(parent, "Dashes").AddComponent<RawImage>();
            line.texture = dash; line.color = new Color(1f, 1f, 1f, 0.35f); line.raycastTarget = false;
            line.uvRect = new Rect(0f, 0f, 1500f / 10f, 1f);
            return line.rectTransform;
        }

        /// <summary>Players' own text (Steam names) shown as written: a '&lt;' in it can't start markup.</summary>
        private static string Plain(string text) => string.IsNullOrEmpty(text) ? text : text.Replace("<", "‹").Replace(">", "›");
    }
}
