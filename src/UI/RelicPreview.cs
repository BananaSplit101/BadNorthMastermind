using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using I2.Loc;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense;
using Voxels.TowerDefense.CampaignGeneration;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    /// <summary>
    /// Relic islands on the campaign map show which relic they hold, for every player (the game only shows a "?" until
    /// the island is won), and hovering one shows the relic's name and description (RelicTooltip).
    /// </summary>
    [HarmonyPatch]
    internal static class RelicPreviewPatch
    {
        private static bool warned;

        private static MethodBase TargetMethod()
        {
            foreach (MethodInfo method in typeof(LevelVisuals).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (method.Name.EndsWith("OnLevelSetup")) return method;
            return null;
        }

        private static void Postfix(LevelVisuals __instance, LevelNode level)
        {
            try
            {
                if (level == null || !level.hasItem || !level.item) return;
                var marker = Traverse.Create(__instance).Field<CanvasGroup>("itemCanvasGroup").Value;
                if (!marker) return;
                // The marker is a hexagon with the "?" drawn into the same picture ("Icon"): the relic takes its place.
                var hexagon = marker.transform.Find("Icon") as RectTransform;
                Show(hexagon ? hexagon : (RectTransform)marker.transform, level.item);
                RelicTooltip.Track(hexagon ? hexagon : (RectTransform)marker.transform, marker, level.item);
            }
            catch (Exception e)
            {
                if (!warned) { warned = true; ModLog.Warn("Couldn't show an island's relic: " + e.Message); }
            }
        }

        /// <summary>The relic drawn like the game's own relic icons (MaskedSprite: cropped to the item shape, with its border).</summary>
        private static void Show(RectTransform hexagon, HeroUpgradeDefinition relic)
        {
            MaskedSprite template = Template();
            if (!template) { Plain(hexagon, relic.GetSprite()); return; }
            var icon = NativeWidgets.GraphicsCopy(template.gameObject, null).GetComponent<MaskedSprite>();
            foreach (var text in icon.GetComponentsInChildren<Text>(true)) UnityEngine.Object.DestroyImmediate(text.gameObject);
            icon.transform.SetParent(hexagon, false);
            var rect = (RectTransform)icon.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            icon.gameObject.SetActive(true);
            foreach (var graphic in icon.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            icon.Set(relic, 0);
            // The "?" picture goes; its children (the relic) stay.
            var question = hexagon.GetComponent<Image>();
            if (question) question.enabled = false;
            // MaskedSprite draws from extra vertex channels the map's canvas may not have on.
            var canvas = hexagon.GetComponentInParent<Canvas>();
            if (canvas) { canvas.additionalShaderChannels |= NativeWidgets.ShaderChannels; canvas.rootCanvas.additionalShaderChannels |= NativeWidgets.ShaderChannels; }
        }

        private static MaskedSprite template;

        /// <summary>One of the game's upgrade icons (the new-game relic carousel's), or any masked icon.</summary>
        private static MaskedSprite Template()
        {
            if (template) return template;
            Type itemIcon = AccessTools.TypeByName("Voxels.TowerDefense.UI.UpgradeCarouselItemIcon");
            foreach (var candidate in Resources.FindObjectsOfTypeAll<MaskedSprite>())
            {
                if (!candidate || !candidate.GetComponent<Image>()) continue;
                if (itemIcon != null && candidate.GetComponentInParent(itemIcon)) { template = candidate; break; }
                if (!template) template = candidate;
            }
            return template;
        }

        /// <summary>Without the game's icon renderer: the relic's picture over the hexagon, a little smaller than it.</summary>
        private static void Plain(RectTransform hexagon, Sprite sprite)
        {
            if (!sprite) return;
            var go = new GameObject("BNMP Relic", typeof(RectTransform));
            go.transform.SetParent(hexagon, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            float size = Mathf.Max(8f, Mathf.Min(hexagon.rect.width, hexagon.rect.height) * 0.62f);
            rect.sizeDelta = new Vector2(size, size);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }
    }

    /// <summary>
    /// Hovering a relic marker on the campaign map shows the relic's name and description. It only reads where the
    /// mouse is (no raycast target), so clicking the island under the marker works as before.
    /// </summary>
    internal class RelicTooltip : MonoBehaviour
    {
        private sealed class Marker { public RectTransform rect; public CanvasGroup group; public HeroUpgradeDefinition relic; }

        private static readonly List<Marker> markers = new List<Marker>();
        private GameObject canvasObject;
        private RectTransform canvasRect, panel;
        private Text title, body;
        private HeroUpgradeDefinition shown;

        public static void Track(RectTransform rect, CanvasGroup group, HeroUpgradeDefinition relic)
        {
            markers.RemoveAll(m => !m.rect || m.rect == rect);
            markers.Add(new Marker { rect = rect, group = group, relic = relic });
        }

        private void Update()
        {
            HeroUpgradeDefinition hovered = null;
            Vector2 mouse = Input.mousePosition;
            for (int i = markers.Count - 1; i >= 0; i--)
            {
                Marker marker = markers[i];
                if (!marker.rect || !marker.relic) { markers.RemoveAt(i); continue; }
                if (!marker.rect.gameObject.activeInHierarchy || (marker.group && marker.group.alpha < 0.5f)) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(marker.rect, mouse, CameraOf(marker.rect))) { hovered = marker.relic; break; }
            }
            if (!hovered)
            {
                if (canvasObject && canvasObject.activeSelf) canvasObject.SetActive(false);
                shown = null;
                return;
            }
            if (!canvasObject) Build();
            if (hovered != shown)
            {
                shown = hovered;
                title.text = hovered.dbgName;
                body.text = Translate(hovered.GetLevelDescription(0));
            }
            if (!canvasObject.activeSelf) canvasObject.SetActive(true);
            Place(mouse);
        }

        private static Camera campaignCamera;
        private static float nextCameraLook;

        /// <summary>
        /// The camera that draws the marker. The map's markers sit in a world-space canvas without an event camera; the
        /// campaign map's own camera (CampaignCameraController) draws them, straight to the screen.
        /// </summary>
        private static Camera CameraOf(RectTransform rect)
        {
            var canvas = rect.GetComponentInParent<Canvas>();
            if (!canvas) return null;
            canvas = canvas.rootCanvas;
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            if (canvas.worldCamera) return canvas.worldCamera;
            if (!campaignCamera && Time.unscaledTime >= nextCameraLook)
            {
                nextCameraLook = Time.unscaledTime + 1f;
                var controller = FindObjectOfType<CampaignCameraController>();
                if (controller) campaignCamera = Traverse.Create(controller).Field<Camera>("cameraRef").Value;
                if (!campaignCamera)
                    foreach (Camera camera in Camera.allCameras)
                        if (camera.enabled && (camera.cullingMask & (1 << rect.gameObject.layer)) != 0) { campaignCamera = camera; break; }
            }
            return campaignCamera;
        }

        private static string Translate(string term)
        {
            if (string.IsNullOrEmpty(term)) return "";
            string text = LocalizationManager.GetTranslation(term);
            return string.IsNullOrEmpty(text) ? term : text;
        }

        /// <summary>Beside the mouse, kept on screen.</summary>
        private void Place(Vector2 mouse)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, mouse, null, out Vector2 local);
            Vector2 size = panel.rect.size, half = canvasRect.rect.size * 0.5f;
            Vector2 at = local + new Vector2(22f, -22f);
            if (at.x + size.x > half.x) at.x = local.x - 22f - size.x;
            if (at.y - size.y < -half.y) at.y = local.y + 22f + size.y;
            panel.anchoredPosition = at;
        }

        private void Build()
        {
            canvasObject = GameStyle.Canvas("BNMP Relic Tooltip", 560);
            canvasRect = (RectTransform)canvasObject.transform;
            canvasObject.GetComponent<GraphicRaycaster>().enabled = false; // never in the way of a click
            panel = GameStyle.Panel(canvasObject.transform, new RectOffset(16, 16, 10, 12), 4f);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0f, 1f);
            title = GameStyle.Label(panel, "", 22);
            body = GameStyle.Label(panel, "", 17);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.alignment = TextAnchor.UpperLeft;
            body.GetComponent<LayoutElement>().preferredWidth = 340f;
            foreach (var graphic in canvasObject.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        }
    }
}
