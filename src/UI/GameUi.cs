using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    /// <summary>Helpers for putting our own content into the game's uGUI widgets.</summary>
    internal static class GameUi
    {
        /// <summary>
        /// A commander's portrait, drawn by the game's own portrait renderer (null if none is loaded). Its canvas needs
        /// NativeWidgets.ShaderChannels.
        /// </summary>
        public static MaskedSprite Portrait(Transform parent, Voxels.TowerDefense.HeroDefinition hero, float size)
        {
            MaskedSprite template = null;
            foreach (var banner in Resources.FindObjectsOfTypeAll<LoadoutUIListBanner>())
            {
                template = Traverse.Create(banner).Field<MaskedSprite>("icon").Value;
                if (template) break;
            }
            if (!template)
                foreach (var candidate in Resources.FindObjectsOfTypeAll<MaskedSprite>())
                    if (candidate.GetComponent<Image>()) { template = candidate; break; }
            if (!template) return null;
            // Only the native portrait renderer belongs here; the banner's labels ("Available Slot") and scripts go.
            var portrait = NativeWidgets.GraphicsCopy(template.gameObject, null).GetComponent<MaskedSprite>();
            foreach (var text in portrait.GetComponentsInChildren<Text>(true)) Object.DestroyImmediate(text.gameObject);
            portrait.transform.SetParent(parent, false);
            var rect = (RectTransform)portrait.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(size, size);
            portrait.gameObject.SetActive(true);
            portrait.Set(hero.graphics);
            return portrait;
        }

        /// <summary>
        /// Gives a game widget a fixed English label. Disabling the Localize component isn't enough: I2's
        /// LocalizeAll re-applies the term to every Localize, enabled or not. So remove it outright.
        /// </summary>
        public static Widget SetPlainLabel(Widget widget, string text)
        {
            if (widget.localize)
            {
                Object.DestroyImmediate(widget.localize);
                widget.localize = null;
            }
            if (widget.label)
                widget.label.text = text;
            return widget;
        }

        // Any real term: popup labels are replaced with plain text right after.
        private const string PlaceholderTerm = "UI/COMMON/OK";

        /// <summary>Fill a pooled game popup with our own title and body. Returns the body Text for updates.</summary>
        public static UnityEngine.UI.Text InitPopup(ModalOverlay popup, string title, string body)
        {
            popup.InitializeNonLocalized(title, body, cancelable: false);
            // Pooled popups keep their Localize components; stop them from overwriting our text.
            var traverse = Traverse.Create(popup);
            traverse.Field<I2.Loc.Localize>("titleLocalize").Value.enabled = false;
            traverse.Field<I2.Loc.Localize>("messageLocalize").Value.enabled = false;
            return traverse.Field<UnityEngine.UI.Text>("message").Value;
        }

        /// <summary>
        /// A popup button. ModalOverlay closes itself when the action returns true; with closes = false it stays
        /// open, and the click sound doubles as the "fail" sound so it still sounds like a normal press.
        /// </summary>
        public static Widget AddPopupButton(ModalOverlay popup, string label, System.Action action, bool closes = false)
        {
            Widget widget = popup.AddButton(PlaceholderTerm, () =>
                {
                    action();
                    return closes;
                })
                .SetFailAudio(FabricID.uiButtonClick)
                .SetHoverAudio(FabricID.uiHover);
            return SetPlainLabel(widget, label);
        }

        /// <summary>Moves <paramref name="widget"/> to just after the menu's widget showing <paramref name="term"/>.</summary>
        public static void PlaceAfter(GeneratedMenu menu, Widget widget, string term)
        {
            var parent = Traverse.Create(menu).Field<RectTransform>("widgetParent").Value;
            if (!parent)
                return;
            foreach (Transform child in parent)
            {
                var other = child.GetComponent<Widget>();
                if (other && other != widget && other.localize && other.localize.Term == term)
                {
                    widget.transform.SetSiblingIndex(child.GetSiblingIndex() + 1);
                    return;
                }
            }
            ModLog.Warn($"Couldn't find '{term}' in {menu.GetType().Name}; Multiplayer button left at the end");
        }
    }
}
