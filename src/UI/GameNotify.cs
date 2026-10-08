using HarmonyLib;
using I2.Loc;
using UnityEngine.UI;
using Voxels.TowerDefense;

namespace BadNorthMP.UI
{
    /// <summary>Shows plain text in the game's in-battle notification banner (the "wave incoming" style).</summary>
    internal static class GameNotify
    {
        // Any real term: the text is replaced right after the banner is set up.
        private const string PlaceholderTerm = "UI/GAMEPLAY/WAVES/FINAL";

        public static void Post(string text, float duration = 4f)
        {
            var manager = Singleton<IslandGameplayManager>.instance;
            if (!manager || !manager.island || !manager.notificationManager)
                return;

            IslandUINotification banner = manager.notificationManager.PostMessage(
                PlaceholderTerm, IslandUINotification.Priority.WaveIncoming, duration);
            Localize localize = Traverse.Create(banner).Field<Localize>("localizeTarget").Value;
            Text label = localize.GetComponent<Text>();
            if (!label)
                label = localize.GetComponentInChildren<Text>();
            if (!label)
                return;
            localize.enabled = false; // re-enabled by the Setup patch when the game reuses this banner
            label.text = text;
        }
    }

    [HarmonyPatch(typeof(IslandUINotification), nameof(IslandUINotification.Setup))]
    internal static class NotificationSetupPatch
    {
        private static void Prefix(Localize ___localizeTarget)
        {
            if (___localizeTarget)
                ___localizeTarget.enabled = true;
        }
    }
}
