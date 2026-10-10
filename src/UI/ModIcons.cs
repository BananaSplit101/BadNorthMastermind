using System;
using UnityEngine;
using UnityEngine.UI;

namespace BadNorthMP.UI
{
    /// <summary>
    /// Small icons the game has no art for (a padlock, an hourglass), drawn in code and smoothed at the edges, and
    /// the badge that shows one in the lower right of a round button.
    /// </summary>
    internal static class ModIcons
    {
        private static Sprite disc, padlock, hourglass, skull;

        public static Sprite Skull => skull ? skull : skull = Draw(64, (x, y) =>
        {
            // A round cranium over a jaw, with eye holes, a nose and teeth cut out.
            float cranium = Coverage(0.29f - Distance(x, y, 0.5f, 0.58f));
            float dx = Mathf.Max(Mathf.Abs(x - 0.5f) - 0.13f, 0f), dy = Mathf.Max(Mathf.Abs(y - 0.31f) - 0.09f, 0f);
            float jaw = Coverage(0.05f - Mathf.Sqrt(dx * dx + dy * dy));
            float eyes = Mathf.Max(Coverage(0.085f - Distance(x, y, 0.385f, 0.56f)), Coverage(0.085f - Distance(x, y, 0.615f, 0.56f)));
            float nose = y < 0.46f ? Coverage(0.04f - Distance(x, y, 0.5f, 0.425f)) : 0f;
            float teeth = 0f;
            if (y > 0.17f && y < 0.31f)
                foreach (float tooth in new[] { 0.44f, 0.5f, 0.56f }) teeth = Mathf.Max(teeth, Coverage(0.012f - Mathf.Abs(x - tooth)));
            return Mathf.Max(cranium, jaw) * (1f - eyes) * (1f - nose) * (1f - teeth);
        });

        public static Sprite Disc => disc ? disc : disc = Draw(64, (x, y) => Coverage(0.5f - Distance(x, y, 0.5f, 0.5f)));

        public static Sprite Padlock => padlock ? padlock : padlock = Draw(64, (x, y) =>
        {
            // Body: a rounded box in the lower part.
            float dx = Mathf.Max(Mathf.Abs(x - 0.5f) - 0.26f, 0f), dy = Mathf.Max(Mathf.Abs(y - 0.33f) - 0.17f, 0f);
            float body = Coverage(0.06f - Mathf.Sqrt(dx * dx + dy * dy));
            // Shackle: a ring over the body (upper half), with straight legs down into it.
            float ring = Mathf.Abs(Distance(x, y, 0.5f, 0.62f) - 0.17f);
            float shackle = y >= 0.62f ? Coverage(0.055f - ring)
                : y >= 0.45f ? Coverage(0.055f - Mathf.Abs(Mathf.Abs(x - 0.5f) - 0.17f)) : 0f;
            return Mathf.Max(body, shackle);
        });

        public static Sprite Hourglass => hourglass ? hourglass : hourglass = Draw(64, (x, y) =>
        {
            // Caps top and bottom.
            float caps = Coverage(0.3f - Mathf.Abs(x - 0.5f)) *
                Mathf.Max(Coverage(0.045f - Mathf.Abs(y - 0.86f)), Coverage(0.045f - Mathf.Abs(y - 0.14f)));
            // The glass: two triangles meeting at the waist, the lower one full of sand, the upper one an outline.
            float height = Mathf.Abs(y - 0.5f);
            float halfWidth = 0.04f + 0.21f * Mathf.Clamp01(height / 0.32f);
            float inside = Coverage(halfWidth - Mathf.Abs(x - 0.5f)) * Coverage(0.32f - height);
            float outline = inside * Coverage(Mathf.Abs(x - 0.5f) - (halfWidth - 0.07f));
            return Mathf.Max(caps, y < 0.5f ? inside : outline);
        });

        /// <summary>A dark disc in the lower right of <paramref name="button"/> with <paramref name="glyph"/> on it; hidden until shown.</summary>
        public static GameObject Badge(Transform button, Sprite glyph, float size)
        {
            var badge = new GameObject("Badge", typeof(RectTransform));
            badge.transform.SetParent(button, false);
            var rect = (RectTransform)badge.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(-size * 0.4f, size * 0.45f);
            rect.sizeDelta = new Vector2(size, size);
            var back = badge.AddComponent<Image>();
            back.sprite = Disc;
            back.color = new Color(0.08f, 0.08f, 0.1f, 0.9f);
            back.raycastTarget = false;
            var icon = new GameObject("Glyph", typeof(RectTransform));
            icon.transform.SetParent(badge.transform, false);
            var iconRect = (RectTransform)icon.transform;
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = Vector2.one * size * 0.2f;
            iconRect.offsetMax = -Vector2.one * size * 0.2f;
            var image = icon.AddComponent<Image>();
            image.sprite = glyph;
            image.color = new Color(1f, 1f, 1f, 0.92f);
            image.raycastTarget = false;
            badge.SetActive(false);
            return badge;
        }

        private static float Distance(float x, float y, float cx, float cy) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

        // How much of a pixel is inside a shape, given how far inside it the pixel's centre is (64 px icons).
        private static float Coverage(float inside) => Mathf.Clamp01(inside * 64f + 0.5f);

        private static Sprite Draw(int size, Func<float, float, float> alpha)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255f * alpha((x + 0.5f) / size, (y + 0.5f) / size)));
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
