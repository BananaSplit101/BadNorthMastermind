using HarmonyLib;
using BadNorthMP.UI;
using UnityEngine;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.Patches
{
    // Both menus build their buttons in code (GeneratedMenu.AddButton), so we add ours the same way and
    // get the game's own button prefab, fonts, sounds and keyboard/gamepad navigation for free.

    [HarmonyPatch(typeof(MainMenu), "Initialize")]
    internal static class MainMenuPatch
    {
        private static void Postfix(MainMenu __instance) => MenuButtons.AddAfter(__instance, "UI/MAIN_MENU/PLAY");
    }

    [HarmonyPatch(typeof(PauseMenu), "Initialize")]
    internal static class PauseMenuPatch
    {
        private static void Postfix(PauseMenu __instance)
        {
            ButtonWidget multiplayer = MenuButtons.AddAfter(__instance, "UI/PAUSE/RESUME");
            MenuButtons.AddCopyLog(__instance, multiplayer);
        }
    }

    internal static class MenuButtons
    {
        public static ButtonWidget AddAfter(GeneratedMenu menu, string term)
        {
            // The term only satisfies AddButton; SetPlainLabel replaces it.
            ButtonWidget button = menu.AddButton(term, MultiplayerScreen.Open);
            GameUi.SetPlainLabel(button, "Multiplayer");
            button.SetHoverAudio(FabricID.uiHover);
            GameUi.PlaceAfter(menu, button, term);
            ModLog.Info($"Added Multiplayer button to {menu.GetType().Name}");
            return button;
        }

        /// <summary>"Copy log", under the Multiplayer button: the whole log on the clipboard for a bug report.</summary>
        public static void AddCopyLog(GeneratedMenu menu, ButtonWidget after)
        {
            ButtonWidget button = null;
            button = menu.AddButton("UI/PAUSE/RESUME", () =>
            {
                bool copied = LogCopy.Copy();
                FabricWrapper.PostEvent(copied ? FabricID.settingChange : FabricID.uiError);
                if (button) ButtonFlash.Show(button, copied ? "Log copied" : "Couldn't copy the log", "Copy log");
                return true;
            });
            GameUi.SetPlainLabel(button, "Copy log");
            button.SetHoverAudio(FabricID.uiHover);
            if (after) button.transform.SetSiblingIndex(after.transform.GetSiblingIndex() + 1);
        }
    }

    /// <summary>A menu button's label says what happened for a moment, then goes back (unscaled time: the game is paused).</summary>
    internal class ButtonFlash : MonoBehaviour
    {
        private ButtonWidget button;
        private string restore;
        private float until;

        public static void Show(ButtonWidget button, string text, string restore)
        {
            var flash = button.GetComponent<ButtonFlash>() ?? button.gameObject.AddComponent<ButtonFlash>();
            flash.button = button;
            flash.restore = restore;
            flash.until = Time.unscaledTime + 2f;
            GameUi.SetPlainLabel(button, text);
        }

        private void Update()
        {
            if (until <= 0f || Time.unscaledTime < until) return;
            until = 0f;
            if (button) GameUi.SetPlainLabel(button, restore);
        }

        // Closing the menu mid-flash: it opens with the usual label next time.
        private void OnDisable()
        {
            if (until <= 0f) return;
            until = 0f;
            if (button) GameUi.SetPlainLabel(button, restore);
        }
    }
}
