using HarmonyLib;
using BadNorthMP.UI;
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
        private static void Postfix(PauseMenu __instance) => MenuButtons.AddAfter(__instance, "UI/PAUSE/RESUME");
    }

    internal static class MenuButtons
    {
        public static void AddAfter(GeneratedMenu menu, string term)
        {
            // The term only satisfies AddButton; SetPlainLabel replaces it.
            ButtonWidget button = menu.AddButton(term, MultiplayerScreen.Open);
            GameUi.SetPlainLabel(button, "Multiplayer");
            button.SetHoverAudio(FabricID.uiHover);
            GameUi.PlaceAfter(menu, button, term);
            ModLog.Info($"Added Multiplayer button to {menu.GetType().Name}");
        }
    }
}
