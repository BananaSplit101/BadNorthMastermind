using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    /// <summary>
    /// The look of the game's own popups for our panels: its parchment panel and menu button sprites and font, plus
    /// the few layout pieces our panels are built from.
    /// </summary>
    internal static class GameStyle
    {
        public static readonly Color PanelColor = new Color(0.76f, 0.72f, 0.66f, 0.97f);
        public static readonly Color ButtonColor = new Color(0.98f, 0.92f, 0.85f, 1f);
        public static readonly Color Ink = new Color(0.2f, 0.17f, 0.15f, 1f);

        public static Font Font { get; private set; }
        public static Sprite ButtonSprite { get; private set; }
        public static Sprite PanelSprite { get; private set; }

        private static void Find()
        {
            if (Font) return;
            Font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            foreach (ModalOverlay popup in Resources.FindObjectsOfTypeAll<ModalOverlay>())
            {
                foreach (Image image in popup.GetComponentsInChildren<Image>(true))
                {
                    var rect = image.rectTransform;
                    if (image.sprite && image.type == Image.Type.Sliced && image.sprite.border.sqrMagnitude > 0f && rect.rect.width > 200f && rect.rect.height > 100f)
                    { PanelSprite = image.sprite; break; }
                }
                if (PanelSprite) break;
            }
            foreach (MainMenu menu in Resources.FindObjectsOfTypeAll<MainMenu>())
            {
                var prefab = Traverse.Create(menu).Field<ButtonWidget>("textButtonPrefab").Value;
                if (!prefab) continue;
                if (prefab.label && prefab.label.font) Font = prefab.label.font;
                foreach (Image image in prefab.GetComponentsInChildren<Image>(true))
                    if (image.sprite) { ButtonSprite = image.sprite; break; }
                break;
            }
        }

        /// <summary>A screen-wide canvas of our own (kept across scenes) that takes clicks.</summary>
        public static GameObject Canvas(string name, int sortingOrder)
        {
            Find();
            var root = new GameObject(name);
            UnityEngine.Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            // The game's portraits (MaskedSprite) draw from these extra vertex channels; without them they come out black.
            canvas.additionalShaderChannels = NativeWidgets.ShaderChannels;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            return root;
        }

        /// <summary>A parchment panel that sizes itself to its contents, stacked top to bottom.</summary>
        public static RectTransform Panel(Transform parent, RectOffset padding, float spacing)
        {
            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(parent, false);
            var background = panel.AddComponent<Image>();
            background.sprite = PanelSprite;
            background.type = PanelSprite ? Image.Type.Sliced : Image.Type.Simple;
            background.color = PanelColor;
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = layout.childForceExpandWidth = false;
            var fitter = panel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            return (RectTransform)panel.transform;
        }

        public static Transform Row(Transform parent, float height, float spacing)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = layout.childForceExpandWidth = false;
            go.AddComponent<LayoutElement>().minHeight = height;
            return go.transform;
        }

        public static Text Label(Transform parent, string text, int size)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.font = Font;
            label.fontSize = size;
            label.color = Ink;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.supportRichText = true;
            label.text = text;
            go.AddComponent<LayoutElement>();
            return label;
        }

        public static Button Button(Transform parent, string text, Action onClick, float width, float height = 42f)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = ButtonSprite;
            image.type = ButtonSprite ? Image.Type.Sliced : Image.Type.Simple;
            image.color = ButtonColor;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width; element.preferredHeight = height;
            Text label = Label(go.transform, text, 22);
            UnityEngine.Object.Destroy(label.GetComponent<LayoutElement>());
            Stretch(label.rectTransform);
            return button;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
