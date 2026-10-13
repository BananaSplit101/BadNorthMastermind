using System;
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

    // The error reports notice, the first time: on the title screen's Start (the button or a gamepad press), then the
    // game carries on. Play and the Multiplayer button ask too, in case it couldn't show there.
    [HarmonyPatch(typeof(AccountLoader), "PlatformReadyActivate")]
    internal static class TitleStartPatch
    {
        private static bool Prefix(AccountLoader __instance, ref bool __result)
        {
            if (!ReportsUi.Ask(() => { if (__instance) Traverse.Create(__instance).Method("PlatformReadyActivate").GetValue(); })) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(MainMenu), "Play")]
    internal static class MainMenuPlayPatch
    {
        private static bool Prefix(MainMenu __instance, ref bool __result)
        {
            if (!ReportsUi.Ask(() => Traverse.Create(__instance).Method("Play").GetValue())) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(PauseMenu), "Initialize")]
    internal static class PauseMenuPatch
    {
        private static void Postfix(PauseMenu __instance)
        {
            ButtonWidget multiplayer = MenuButtons.AddAfter(__instance, "UI/PAUSE/RESUME");
            ButtonWidget copyLog = MenuButtons.AddCopyLog(__instance, multiplayer);
            MenuButtons.AddReportBug(__instance, copyLog);
        }
    }

    internal static class MenuButtons
    {
        public static ButtonWidget AddAfter(GeneratedMenu menu, string term)
        {
            ButtonWidget button = Add(menu, term, "Multiplayer", MultiplayerScreen.Open);
            GameUi.PlaceAfter(menu, button, term);
            ModLog.Info($"Added Multiplayer button to {menu.GetType().Name}");
            return button;
        }

        /// <summary>"Copy log", under the Multiplayer button: the whole log on the clipboard for a bug report.</summary>
        public static ButtonWidget AddCopyLog(GeneratedMenu menu, ButtonWidget after)
        {
            ButtonWidget button = null;
            button = Add(menu, "UI/PAUSE/RESUME", "Copy log", () =>
            {
                bool copied = LogCopy.Copy();
                FabricWrapper.PostEvent(copied ? FabricID.settingChange : FabricID.uiError);
                if (button) ButtonFlash.Show(button, copied ? "Log copied" : "Couldn't copy the log", "Copy log");
                return true;
            }, after);
            return button;
        }

        /// <summary>"Report bug", under Copy log: what happened, sent with everyone in the lobby's logs.</summary>
        public static void AddReportBug(GeneratedMenu menu, ButtonWidget after) =>
            Add(menu, "UI/PAUSE/RESUME", "Report bug", ReportsUi.OpenBugReport, after);

        // The term only satisfies AddButton; the plain label replaces it.
        private static ButtonWidget Add(GeneratedMenu menu, string term, string label, Func<bool> action, ButtonWidget after = null)
        {
            ButtonWidget button = menu.AddButton(term, action);
            GameUi.SetPlainLabel(button, label);
            button.SetHoverAudio(FabricID.uiHover);
            if (after) button.transform.SetSiblingIndex(after.transform.GetSiblingIndex() + 1);
            return button;
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
            if (Time.unscaledTime >= until) Restore();
        }

        // Closing the menu mid-flash: it opens with the usual label next time.
        private void OnDisable() => Restore();

        private void Restore()
        {
            if (until <= 0f) return;
            until = 0f;
            if (button) GameUi.SetPlainLabel(button, restore);
        }
    }
}
