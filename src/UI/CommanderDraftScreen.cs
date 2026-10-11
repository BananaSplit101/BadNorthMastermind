using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BadNorthMP.Net;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense;
using Voxels.TowerDefense.HeroGeneration;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    internal sealed class CommanderDraftScreen : MonoBehaviour
    {
        private static CommanderDraftScreen instance;
        private GameObject canvasObject, panel, grid;
        private Font font;
        private Sprite buttonSprite;
        private Text title, roster, hint, continueText;
        private Button ready;
        
        // One card per commander slot shown, in order. A new portrait rebuilds only that card (the old one stays up
        // until the new one is ready); a new trait or item is shown on the card in place.
        private sealed class Slot
        {
            public ulong player;
            public int index, choice;
            public string trait, item;
            public HeroDefinition hero;
            public NativeCommanderCard card;
        }
        private readonly List<Slot> rendered = new List<Slot>();
        private readonly List<Slot> building = new List<Slot>();
        private string renderedLayout;
        private bool baking;
        private string previewError;
        private Coroutine generation;
        private readonly Color ink = new Color(0.2f, 0.17f, 0.15f);
        private readonly Color paper = new Color(0.98f, 0.92f, 0.85f);
        
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
            title.text = continuing ? "Continue Campaign " + (PartyFlow.State.continuing + 1) : "Choose your commanders";
            if (continueText.gameObject.activeSelf != continuing) continueText.gameObject.SetActive(continuing);
            grid.SetActive(wanted.Count > 0);
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
            var body = new StringBuilder(); body.AppendLine(PartyFlow.Status);
            foreach (var player in PartyFlow.State.seats)
                body.Append(SteamSession.NameOf(new CSteamID(player.player)))
                    .Append(continuing ? "" : " · " + player.heroes.Length + (player.heroes.Length == 1 ? " commander" : " commanders"))
                    .AppendLine(player.ready ? " · Ready" : continuing ? " · Not ready" : " · Choosing");
            roster.text = body.ToString();
            hint.text = continuing ? (seat == null ? "Waiting for the defenders to press Ready." : "Press Ready to load the campaign.")
                : previewError ?? (seat == null ? "Defenders choose their portraits, starting traits and items, then press Ready."
                : "Choose your portrait, starting trait and item with the arrows.\nYour squad starts as militia. Classes are purchased during the campaign.");
            ready.gameObject.SetActive(seat != null);
            ready.interactable = seat != null && !PartyFlow.PendingPick && (continuing || (!baking && (previewError != null || rendered.Count == wanted.Count)));
            ready.GetComponentInChildren<Text>().text = previewError != null && !continuing ? "Retry cards" : PartyFlow.PendingPick ? "Confirming..." : seat != null && seat.ready ? "Unready" : "Ready";
        }
        private static List<DefenderSeat> Selections()
        {
            var result = new List<DefenderSeat>();
            if (SteamSession.Instance.IsHost) result.AddRange(PartyFlow.State.seats);
            else if (PartyFlow.Mine != null) result.Add(PartyFlow.Mine);
            return result;
        }
        /// <summary>The cards that should be up: every commander slot of the seats shown, with its current picks.</summary>
        private static List<Slot> Wanted()
        {
            var wanted = new List<Slot>();
            foreach (var seat in Selections())
                for (int i = 0; i < seat.choices.Length; i++)
                    wanted.Add(new Slot { player = seat.player, index = i, choice = seat.choices[i], trait = seat.traits[i], item = seat.items[i] });
            return wanted;
        }

        /// <summary>Which slots are shown (not what's picked in them): when this changes, every card is rebuilt.</summary>
        private static string Layout(List<Slot> wanted)
        {
            var key = new StringBuilder().Append(PartyFlow.State.token).Append('|');
            foreach (Slot slot in wanted) key.Append(slot.player).Append(':').Append(slot.index).Append(';');
            return key.ToString();
        }

        /// <summary>Bring the cards up to date with the picks: a new trait or item in place, a new portrait by rebuilding that card.</summary>
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
            float width = wanted.Count > 2 ? 210 : 310;
            foreach (int i in indices)
            {
                Slot slot = wanted[i];
                int index = slot.index;
                bool own = slot.player == SteamSession.Instance.Me.m_SteamID;
                try
                {
                    slot.card = NativeCommanderCard.Create(grid.transform, slot.hero,
                        own ? (Action<int>)(step => PartyFlow.ChooseCommander(index, (PartyFlow.Mine.choices[index] + step + PartyRoster.ChoiceCount) % PartyRoster.ChoiceCount)) : null,
                        own ? (Action<int>)(step => PartyFlow.CycleUpgrade(index, true, step)) : null,
                        own ? (Action<int>)(step => PartyFlow.CycleUpgrade(index, false, step)) : null,
                        width);
                    if (slot.card == null) throw new Exception("The native commander card could not be loaded.");
                }
                catch (Exception e) { PreviewFailed(e); }
                building.Remove(slot);
                if (slot.card == null) { Release(slot); continue; }
                if (only == null) { rendered.Add(slot); continue; }
                // Swap the new card in where the old one was.
                Slot old = rendered[i];
                slot.card.root.transform.SetSiblingIndex(old.card.root.transform.GetSiblingIndex());
                Release(old);
                rendered[i] = slot;
            }
            baking = false; generation = null;
        }

        /// <summary>
        /// What a card was made from and a short code for the face it got, so two players' logs show whether their
        /// games drew the same commander for the same pick (and if not, whether the picks or the drawing differed).
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
            foreach (Slot slot in rendered) Release(slot);
            rendered.Clear();
            baking = false; renderedLayout = null;
        }
        private void OnDestroy() { ClearPreviews(); if (canvasObject) Destroy(canvasObject); }
        private void Build()
        {
            foreach (var menu in Resources.FindObjectsOfTypeAll<MainMenu>())
            {
                var prefab = Traverse.Create(menu).Field<ButtonWidget>("textButtonPrefab").Value;
                if (!prefab) continue;
                if (prefab.label) font = prefab.label.font;
                foreach (var image in prefab.GetComponentsInChildren<Image>(true)) if (image.sprite) { buttonSprite = image.sprite; break; }
                if (font) break;
            }
            if (!font) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            canvasObject = new GameObject("BNMP Commander Selection", typeof(RectTransform)); DontDestroyOnLoad(canvasObject);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent; canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 650;
            var scaler = canvasObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1;
            canvasObject.AddComponent<GraphicRaycaster>();
            var shade = Node(canvasObject.transform, "Backdrop"); var shadeRect = (RectTransform)shade.transform;
            shadeRect.anchorMin = Vector2.zero; shadeRect.anchorMax = Vector2.one; shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;
            shade.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 0.78f);
            panel = Node(canvasObject.transform, "Commander Menu"); var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f); rect.sizeDelta = new Vector2(1000, 900);
            panel.AddComponent<Image>().color = new Color(0.76f, 0.72f, 0.66f);
            var layout = panel.AddComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(30, 30, 24, 24);
            layout.spacing = 12; layout.childForceExpandHeight = false;
            title = Label(panel.transform, "Choose your commanders", 40, 58);
            roster = Label(panel.transform, "", 22, 160);
            continueText = Label(panel.transform,
                "Your commanders come from the save, with the looks, traits, items and upgrades they already have.\n\n" +
                "Players who led commanders in this campaign before keep them. Commanders whose players aren't here are " +
                "shared out among the defenders who are.", 24, 220);
            continueText.horizontalOverflow = HorizontalWrapMode.Wrap;
            continueText.gameObject.SetActive(false);
            grid = Node(panel.transform, "Native Commander Cards"); grid.AddComponent<LayoutElement>().preferredHeight = 430;
            var cardLayout = grid.AddComponent<HorizontalLayoutGroup>(); cardLayout.spacing = 12;
            cardLayout.childAlignment = TextAnchor.MiddleCenter; cardLayout.childForceExpandWidth = false;
            cardLayout.childForceExpandHeight = false; cardLayout.childControlWidth = true; cardLayout.childControlHeight = true;
            hint = Label(panel.transform, "", 22, 65);
            var footer = Node(panel.transform, "Actions"); footer.AddComponent<HorizontalLayoutGroup>().spacing = 12;
            footer.AddComponent<LayoutElement>().preferredHeight = 52;
            ready = Button(footer.transform, "Ready", Confirm);
            if (SteamSession.Instance.IsHost) Button(footer.transform, "Back", PartyFlow.CancelDraft);
            Button(footer.transform, "Leave lobby", SteamSession.Instance.Leave);
        }
        private GameObject Node(Transform parent, string name)
        { var node = new GameObject(name, typeof(RectTransform)); node.transform.SetParent(parent, false); return node; }
        private Text Label(Transform parent, string value, int size, float height)
        {
            var node = Node(parent, "Label"); var text = node.AddComponent<Text>(); text.font = font; text.fontSize = size;
            text.color = ink; text.text = value; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            node.AddComponent<LayoutElement>().preferredHeight = height; return text;
        }
        private Button Button(Transform parent, string value, UnityEngine.Events.UnityAction action)
        {
            var node = Node(parent, "Choice"); var image = node.AddComponent<Image>(); image.sprite = buttonSprite;
            image.type = buttonSprite ? Image.Type.Sliced : Image.Type.Simple; image.color = paper;
            var button = node.AddComponent<Button>(); button.targetGraphic = image;
            button.onClick.AddListener(() => { FabricWrapper.PostEvent(FabricID.uiButtonClick); action(); });
            var label = Label(node.transform, value, 23, 0); var rect = (RectTransform)label.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            node.AddComponent<LayoutElement>().flexibleWidth = 1; return button;
        }
    }
}
