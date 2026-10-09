using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    /// <summary>Helpers for putting our own content into the game's uGUI widgets.</summary>
    internal static class GameUi
    {
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
