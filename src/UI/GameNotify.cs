using System.Collections.Generic;
using HarmonyLib;
using I2.Loc;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense;

namespace BadNorthMP.UI
{
    /// <summary>
    /// Plain text in the game's in-battle notification banner ("wave incoming" style), or, on the campaign map (no
    /// banner), a plain notice at the top of the screen.
    /// </summary>
    internal static class GameNotify
    {
        // Any real term: the text is replaced right after the banner is set up.
        private const string PlaceholderTerm = "UI/GAMEPLAY/WAVES/FINAL";
        private static readonly List<KeyValuePair<string, float>> waiting = new List<KeyValuePair<string, float>>();

        public static void Post(string text, float duration = 4f)
        {
            var manager = Singleton<IslandGameplayManager>.instance;
            if (!manager || !manager.island || !manager.notificationManager)
            {
                MapNotice.Show(text, duration);
                return;
            }
            // Setting up a banner animates it, which fails while its display is still switched off.
            if (!manager.notificationManager.isActiveAndEnabled)
            {
                if (waiting.Count < 4) waiting.Add(new KeyValuePair<string, float>(text, duration));
                return;
            }
            Show(manager, text, duration);
        }

        /// <summary>Every frame (MultiplayerGame): the banners that waited, once the display is up.</summary>
        public static void Update()
        {
            if (waiting.Count == 0) return;
            var manager = Singleton<IslandGameplayManager>.instance;
            if (!manager || !manager.island || !manager.notificationManager)
            {
                waiting.Clear(); // the island went away: they're out of date
                return;
            }
            if (!manager.notificationManager.isActiveAndEnabled) return;
            foreach (var banner in waiting) Show(manager, banner.Key, banner.Value);
            waiting.Clear();
        }

        private static void Show(IslandGameplayManager manager, string text, float duration)
        {
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

    internal class MapNotice : MonoBehaviour
    {
        private static MapNotice instance;
        private Text label;
        private float hideAt;

        public static void Show(string text, float duration)
        {
            if (!instance)
            {
                GameObject root = GameStyle.Canvas("BNMP Map Notice", 600);
                root.GetComponent<GraphicRaycaster>().enabled = false;
                instance = root.AddComponent<MapNotice>();
                var node = new GameObject("Text");
                node.transform.SetParent(root.transform, false);
                instance.label = node.AddComponent<Text>();
                instance.label.font = GameStyle.Font;
                instance.label.fontSize = 30;
                instance.label.alignment = TextAnchor.UpperCenter;
                instance.label.color = Color.white;
                instance.label.raycastTarget = false;
                node.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.8f);
                RectTransform rect = instance.label.rectTransform;
                rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(0.5f, 1);
                // Below the party vote header (VotePanel).
                rect.anchoredPosition = new Vector2(0, -180); rect.sizeDelta = new Vector2(-120, 140);
            }
            instance.label.text = text;
            instance.label.gameObject.SetActive(true);
            instance.hideAt = Time.unscaledTime + duration;
        }

        private void Update()
        {
            if (label.gameObject.activeSelf && Time.unscaledTime >= hideAt) label.gameObject.SetActive(false);
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
