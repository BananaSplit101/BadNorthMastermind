using System;
using System.Collections.Generic;
using HarmonyLib;
using I2.Loc;
using RTM.UISystem;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    // Clone the actual new-game commander card, retaining its artwork and geometry only.
    internal sealed class NativeCommanderCard
    {
        public GameObject root;

        // The starting trait and item shown on the card (name and icon), updated in place when they change.
        private sealed class Carousel { public bool item; public Text name; public MaskedSprite icon; public Sprite emptySprite, emptyMask; }
        private readonly List<Carousel> carousels = new List<Carousel>();

        /// <summary>Show the hero's current starting trait and item (no rebuild: they don't change the portrait).</summary>
        public void SetUpgrades(HeroDefinition hero)
        {
            foreach (Carousel carousel in carousels)
            {
                var selected = carousel.item ? hero.itemUpgrade : hero.traitUpgrade;
                Show(carousel, selected == null ? null : selected.definition);
            }
        }

        private static void Show(Carousel carousel, HeroUpgradeDefinition definition)
        {
            if (carousel.name) carousel.name.text = definition ? definition.dbgName : carousel.item ? "---No Item---" : "---No Trait---";
            if (!carousel.icon) return;
            carousel.icon.sprite = carousel.emptySprite;
            carousel.icon.mask = carousel.emptyMask;
            carousel.icon.foreground = carousel.icon.background = null;
            if (definition) carousel.icon.Set(definition, 0);
        }

        private static void WireArrow(GameObject arrow, Transform root, Action action)
        {
            ActivateParents(arrow.transform, root);
            var button = arrow.AddComponent<Button>();
            button.targetGraphic = arrow.GetComponent<Graphic>() ?? arrow.GetComponentInChildren<Graphic>(true);
            if (button.targetGraphic) button.targetGraphic.raycastTarget = true;
            button.interactable = action != null;
            if (action != null)
            {
                button.onClick.AddListener(() => { FabricWrapper.PostEvent(FabricID.settingChange); action(); });
                // The arrows are small: an invisible area around each takes its clicks too.
                var hit = new GameObject("Hit area", typeof(RectTransform));
                hit.transform.SetParent(arrow.transform, false);
                var hitRect = (RectTransform)hit.transform;
                hitRect.anchorMin = Vector2.zero; hitRect.anchorMax = Vector2.one;
                Vector2 pad = Vector2.Max(((RectTransform)arrow.transform).rect.size * 0.6f, new Vector2(24f, 24f));
                hitRect.offsetMin = -pad; hitRect.offsetMax = pad;
                var area = hit.AddComponent<Image>();
                area.color = Color.clear;
                area.raycastTarget = true;
            }
        }
        private static void ActivateParents(Transform child, Transform stop)
        {
            for (var current = child; current && current != stop; current = current.parent)
                current.gameObject.SetActive(true);
        }
        public static NativeCommanderCard Create(Transform parent, HeroDefinition hero, Action<int> cycle, Action<int> traitCycle, Action<int> itemCycle, float cardWidth = 310)
        {
            var type = AccessTools.TypeByName("Voxels.TowerDefense.UI.NewGameHeroSelector");
            Component template = null;
            if (type != null)
                foreach (var candidate in Resources.FindObjectsOfTypeAll(type))
                    if (candidate is Component component && Traverse.Create(component).Field<HeroCustomizer>("heroCustomizer").Value)
                    { template = component; break; }
            if (!template)
            {
                ModLog.Warn("Native commander card is unavailable; selection remains unconfirmed.");
                return null;
            }
            // The arrow tabs' slid-out sizes, read off the game's card before it's copied (see CardTabs).
            var slidOut = CardTabs.SlidOutSizes(template.transform);
            var holder = new GameObject("Commander Card", typeof(RectTransform));
            holder.transform.SetParent(parent, false); holder.SetActive(false);
            var element = holder.AddComponent<LayoutElement>(); element.preferredWidth = cardWidth; element.preferredHeight = 420;
            var clone = UnityEngine.Object.Instantiate(template.gameObject, holder.transform, false);
            var customizer = clone.GetComponentInChildren<HeroCustomizer>(true);
            var portrait = Traverse.Create(customizer).Field<MaskedSprite>("portraitSprite").Value;
            var name = Traverse.Create(customizer).Field<Localize>("nameLocalize").Value.GetComponent<Text>();
            var arrows = new List<KeyValuePair<GameObject, int>>();
            // Serialized arrow actions identify the exact top pair, avoiding guesses about child names.
            foreach (var click in clone.GetComponentsInChildren<UIClickable>(true))
            {
                var actions = Traverse.Create(click).Field<UnityEvent>("onClickEvent").Value;
                if (actions == null) continue;
                for (int i = 0; i < actions.GetPersistentEventCount(); i++)
                {
                    if (actions.GetPersistentTarget(i) != customizer) continue;
                    string method = actions.GetPersistentMethodName(i);
                    if (method == "DecrementHandler" || method == "IncrementHandler")
                        arrows.Add(new KeyValuePair<GameObject, int>(click.gameObject, method == "DecrementHandler" ? -1 : 1));
                }
            }
            if (arrows.Count == 0)
                foreach (var transform in clone.GetComponentsInChildren<RectTransform>(true))
                    if (transform.name.IndexOf("arrow", StringComparison.OrdinalIgnoreCase) >= 0 && transform.GetComponent<Graphic>() && !transform.GetComponentInParent<UpgradeCarousel>())
                        arrows.Add(new KeyValuePair<GameObject, int>(transform.gameObject,
                            customizer.transform.InverseTransformPoint(transform.position).x < 0 ? -1 : 1));
            var upgradeArrows = new List<KeyValuePair<GameObject, Action>>();
            var card = new NativeCommanderCard();
            // A carousel's entries aren't all inside it: the item carousel shows icons, and its name is drawn by an
            // UpgradeCarouselSlave elsewhere on the card that mirrors it (the "driver").
            var slaves = new List<KeyValuePair<UpgradeCarousel, Transform>>();
            var slaveType = AccessTools.TypeByName("Voxels.TowerDefense.UI.UpgradeCarouselSlave");
            if (slaveType != null)
                foreach (var slave in clone.GetComponentsInChildren(slaveType, true))
                {
                    var driver = Traverse.Create(slave).Field<UpgradeCarousel>("driver").Value;
                    if (driver) slaves.Add(new KeyValuePair<UpgradeCarousel, Transform>(driver, slave.transform));
                }
            // Snapshot the carousel references before removing their controllers.
            foreach (var carousel in clone.GetComponentsInChildren<UpgradeCarousel>(true))
            {
                var previous = Traverse.Create(carousel).Field<UIClickable>("prevArrow").Value;
                var next = Traverse.Create(carousel).Field<UIClickable>("nextArrow").Value;
                bool item = carousel == Traverse.Create(clone.GetComponent(type)).Field<UpgradeCarousel>("itemCarousel").Value;
                var selectedUpgrade = item ? hero.itemUpgrade : hero.traitUpgrade;
                var definition = selectedUpgrade == null ? null : selectedUpgrade.definition;
                Action<int> cycleUpgrade = item ? itemCycle : traitCycle;
                if (previous) upgradeArrows.Add(new KeyValuePair<GameObject, Action>(previous.gameObject,
                    cycleUpgrade == null ? (Action)null : () => cycleUpgrade(-1)));
                if (next) upgradeArrows.Add(new KeyValuePair<GameObject, Action>(next.gameObject,
                    cycleUpgrade == null ? (Action)null : () => cycleUpgrade(1)));
                // Native carousel entries are pooled and may be hidden or slid out of view.
                var shown = new Carousel { item = item };
                card.carousels.Add(shown);
                bool shownName = false, shownIcon = false;
                var roots = new List<Transform> { carousel.transform };
                foreach (var slave in slaves) if (slave.Key == carousel) roots.Add(slave.Value);
                foreach (Transform root in roots)
                foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (script.GetType().Name == "UpgradeCarouselItemName")
                    {
                        script.gameObject.SetActive(!shownName);
                        if (shownName) continue;
                        shownName = true;
                        // The label the game writes the name into: the Text with the Localize on it.
                        var localize = script.GetComponentInChildren<Localize>(true);
                        var text = localize ? localize.GetComponent<Text>() : script.GetComponentInChildren<Text>(true);
                        if (text)
                        {
                            text.gameObject.SetActive(true);
                            text.color = new Color(0.2f, 0.17f, 0.15f, 1);
                            shown.name = text;
                        }
                        var entry = (RectTransform)script.transform;
                        entry.pivot = new Vector2(0.5f, 0.5f);
                        entry.anchoredPosition = Vector2.zero;
                        ActivateParents(script.transform, root);
                    }
                    else if (script.GetType().Name == "UpgradeCarouselItemIcon")
                    {
                        script.gameObject.SetActive(!shownIcon);
                        if (shownIcon) continue;
                        shownIcon = true;
                        var icon = script.GetComponentInChildren<MaskedSprite>(true);
                        if (!icon) continue;
                        shown.icon = icon;
                        shown.emptySprite = Traverse.Create(script).Field<Sprite>("noItemSprite").Value;
                        shown.emptyMask = Traverse.Create(script).Field<Sprite>("noItemMask").Value;
                        icon.gameObject.SetActive(true); icon.transform.localScale = Vector3.one;
                        var entry = (RectTransform)script.transform;
                        entry.anchoredPosition = Vector2.zero;
                        ActivateParents(script.transform, root);
                    }
                }
                if (!shownName) ModLog.Warn($"Commander card: no {(item ? "item" : "trait")} name label found; its name won't update");
                Show(shown, definition);
            }
            // The player's own card: its arrow tabs move as on the game's new-game cards (the animator is about to go).
            if (cycle != null) CardTabs.Attach(holder, clone, slidOut);
            var deluxe = Traverse.Create(clone.GetComponent(type)).Field<BoolWidget>("deluxeDLCWidget").Value;
            if (deluxe) deluxe.gameObject.SetActive(false);
            foreach (var script in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (script is Graphic || script is IMeshModifier || script is Mask || script is RectMask2D || script is LayoutGroup || script is LayoutElement || script is ContentSizeFitter) continue;
                // No original callbacks, local hero randomizer, upgrades, menu registration, or DLC changes survive.
                script.enabled = false;
                UnityEngine.Object.DestroyImmediate(script);
            }
            foreach (var graphic in clone.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            foreach (var canvas in clone.GetComponentsInChildren<Canvas>(true))
            { canvas.overrideSorting = false; canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent; }
            foreach (var group in clone.GetComponentsInChildren<CanvasGroup>(true))
            { group.alpha = 1; group.interactable = true; group.blocksRaycasts = true; }
            portrait.Set(hero.graphics); portrait.saturation = 1; name.text = hero.dbgName;
            foreach (var banner in clone.GetComponentsInChildren<BannerPolygon>(true)) banner.Setup(hero);
            var rect = (RectTransform)clone.transform;
            var originalRect = (RectTransform)template.transform;
            float width = Mathf.Max(1, originalRect.rect.width), height = Mathf.Max(1, originalRect.rect.height);
            float scale = Mathf.Min(cardWidth / width, 420f / height);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = Vector2.zero; rect.localScale = Vector3.one * scale;
            foreach (var arrow in arrows)
            {
                int step = arrow.Value;
                WireArrow(arrow.Key, clone.transform, cycle == null ? (Action)null : () => cycle(step));
            }
            foreach (var arrow in upgradeArrows) WireArrow(arrow.Key, clone.transform, arrow.Value);
            if (arrows.Count < 2)
            {
                ModLog.Warn("Native commander portrait arrows could not be resolved.");
                UnityEngine.Object.Destroy(holder); return null;
            }
            clone.SetActive(true); holder.SetActive(true);
            card.root = holder;
            return card;
        }
    }
}
