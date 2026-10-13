using System;
using HarmonyLib;
using I2.Loc;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Voxels.TowerDefense.UI;
using UIState = RTM.UISystem.UIInteractable.State;

namespace BadNorthMP.UI
{
    /// <summary>
    /// Graphics-only copies of the game's battle widgets for our own screens: ability logic, input, pooling and
    /// localisation are stripped (the round button keeps its native animator).
    /// </summary>
    internal static class NativeWidgets
    {
        /// <summary>Shader channels the game's masked-sprite and banner materials read; set on any canvas showing copies.</summary>
        public const AdditionalCanvasShaderChannels ShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3 |
            AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;

        private static Transform staging;
        private static ActiveAbilityButton abilityButton;
        private static MaskedSprite coin;
        private static float nextSearch;
        private static bool reported;

        public static bool HasRoundButton
        {
            get { FindTemplates(); return abilityButton; }
        }
        public static ActiveAbilityButton RoundButtonTemplate => abilityButton;

        /// <summary>The templates load with the island UI; search at most every two seconds until found.</summary>
        private static void FindTemplates()
        {
            if ((abilityButton && coin) || Time.unscaledTime < nextSearch) return;
            nextSearch = Time.unscaledTime + 2f;
            if (!abilityButton)
                foreach (ActiveAbilityButton candidate in Resources.FindObjectsOfTypeAll<ActiveAbilityButton>())
                {
                    var visual = candidate ? candidate.GetComponent<ActiveAbilityButtonVisual>() : null;
                    if (visual && Traverse.Create(visual).Field<MaskedSpriteAnimated>("maskedSpriteAnimated").Value &&
                        Traverse.Create(visual).Field<Image>("iconImage").Value)
                    { abilityButton = candidate; break; }
                }
            if (!coin)
                foreach (CoinGraphic candidate in Resources.FindObjectsOfTypeAll<CoinGraphic>())
                {
                    var sprite = candidate ? Traverse.Create(candidate).Field<MaskedSprite>("maskedSprite").Value : null;
                    if (sprite && sprite.GetComponent<Image>()) { coin = sprite; break; }
                }
            if (!reported && abilityButton && coin)
            {
                reported = true;
                ModLog.Info("Mastermind HUD: using the game's ability button and coin graphics");
            }
        }

        /// <summary>Copy without waking anything up: instantiate under an inactive parent, then strip scripts.</summary>
        public static GameObject GraphicsCopy(GameObject source, Transform parent, params Type[] keep)
        {
            if (!staging)
            {
                var go = new GameObject("BNMP UI Staging");
                go.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(go);
                staging = go.transform;
            }
            GameObject clone = UnityEngine.Object.Instantiate(source, staging, false);
            Strip(clone, keep);
            foreach (Graphic graphic in clone.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            foreach (Canvas canvas in clone.GetComponentsInChildren<Canvas>(true))
            { canvas.overrideSorting = false; canvas.additionalShaderChannels = ShaderChannels; }
            foreach (CanvasGroup group in clone.GetComponentsInChildren<CanvasGroup>(true))
            { group.alpha = 1f; group.interactable = true; group.blocksRaycasts = false; }
            // Without a parent the copy waits, inactive, in staging (so kept scripts don't wake up yet).
            if (parent) clone.transform.SetParent(parent, false);
            return clone;
        }

        /// <summary>Remove every script but graphics, layout and the given types (several passes: a script can't go while another requires it).</summary>
        public static void Strip(GameObject copy, params Type[] keep)
        {
            for (int pass = 0; pass < 3; pass++)
                foreach (MonoBehaviour script in copy.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!script || script is Graphic || script is IMeshModifier || script is Mask || script is RectMask2D ||
                        script is LayoutGroup || script is LayoutElement || script is ContentSizeFitter) continue;
                    if (Array.Exists(keep, k => k.IsInstanceOfType(script))) continue;
                    try { UnityEngine.Object.DestroyImmediate(script); } catch (Exception) { }
                }
        }

        /// <summary>Scale and centre a copy so <paramref name="fit"/> (a part of it) fills <paramref name="size"/>.</summary>
        public static void Fit(RectTransform copy, RectTransform fit, float size, float fallbackSize)
        {
            copy.anchorMin = copy.anchorMax = copy.pivot = new Vector2(0.5f, 0.5f);
            copy.localScale = Vector3.one;
            copy.localRotation = Quaternion.identity;
            if (copy.rect.width < 1f || copy.rect.height < 1f) copy.sizeDelta = new Vector2(fallbackSize, fallbackSize);
            copy.anchoredPosition = Vector2.zero;
            Rect area = fit ? fit.rect : copy.rect;
            float extent = Mathf.Max(area.width, area.height);
            if (extent < 1f) extent = fallbackSize;
            float scale = size / extent;
            Vector2 centre = fit ? (Vector2)copy.InverseTransformPoint(fit.TransformPoint(area.center)) : Vector2.zero;
            copy.localScale = Vector3.one * scale;
            copy.anchoredPosition = -centre * scale;
        }

        /// <summary>The game's coin, at <paramref name="size"/>; a gold dot if the coin isn't loaded.</summary>
        public static Graphic Coin(Transform parent, float size)
        {
            FindTemplates();
            var holder = new GameObject("Coin", typeof(RectTransform));
            holder.transform.SetParent(parent, false);
            ((RectTransform)holder.transform).sizeDelta = new Vector2(size, size);
            var element = holder.AddComponent<LayoutElement>();
            element.preferredWidth = element.minWidth = element.preferredHeight = element.minHeight = size;
            if (coin)
            {
                GameObject copy = GraphicsCopy(coin.gameObject, holder.transform);
                foreach (Text text in copy.GetComponentsInChildren<Text>(true)) text.gameObject.SetActive(false);
                foreach (Transform child in copy.GetComponentsInChildren<Transform>(true))
                    if (child != copy.transform && child.localScale.sqrMagnitude < 0.01f) child.localScale = Vector3.one;
                copy.SetActive(true);
                Fit((RectTransform)copy.transform, null, size, 60f);
                return copy.GetComponent<Graphic>();
            }
            var dot = holder.AddComponent<Image>();
            dot.color = new Color(1f, 0.8f, 0.3f, 1f);
            dot.raycastTarget = false;
            return dot;
        }

        /// <summary>White text with the soft drop shadow the game uses for words over the island.</summary>
        public static Text Text(Transform parent, Font font, string text, int size, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.color = Color.white;
            label.alignment = anchor;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = true;
            label.text = text;
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(1.5f, -2f);
            go.AddComponent<LayoutElement>();
            return label;
        }
    }

    /// <summary>
    /// A copy of the round ability button from a selected squad's banner, with its native hover, press, selected and
    /// greyed-out animation, the "x2" charge bubble and the hover tooltip.
    /// </summary>
    internal sealed class NativeRoundButton
    {
        public GameObject root;
        public Text caption;
        private MaskedSpriteAnimated anim;
        private MaskedSprite disc;
        private Image icon;
        private GameObject bubble, tooltip;
        private Text bubbleText, tooltipText;
        private Image fallbackDisc;
        private string tooltipValue;

        public bool Selected { set { if (anim) anim.selected = value; else if (fallbackDisc) fallbackDisc.color = value ? new Color(1f, 0.8f, 0.3f, 0.9f) : new Color(0f, 0f, 0f, 0.45f); } }
        public bool Available { set { if (anim) anim.avaliable = value; if (icon) icon.color = value || anim ? Color.white : new Color(1f, 1f, 1f, 0.45f); } }

        public static NativeRoundButton Create(Transform parent, float size, Font font, Action<PointerEventData.InputButton> onClick)
        {
            var holder = new GameObject("Round button", typeof(RectTransform));
            holder.transform.SetParent(parent, false);
            ((RectTransform)holder.transform).sizeDelta = new Vector2(size, size);
            var element = holder.AddComponent<LayoutElement>();
            element.preferredWidth = element.minWidth = element.preferredHeight = element.minHeight = size;
            // An invisible hit area: the copy's own graphics never take clicks.
            var hit = holder.AddComponent<Image>();
            hit.color = Color.clear;
            var button = new NativeRoundButton { root = holder };
            var input = holder.AddComponent<RoundButtonInput>();
            input.owner = button;
            input.onClick = onClick;

            if (NativeWidgets.HasRoundButton) button.Copy(size);
            else button.Fallback();

            button.caption = NativeWidgets.Text(holder.transform, font, "", Mathf.RoundToInt(Mathf.Clamp(size * 0.24f, 15f, 21f)));
            var rect = button.caption.rectTransform;
            button.caption.GetComponent<LayoutElement>().ignoreLayout = true;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -2f);
            // One line tall: the text is centred in its box, so a taller box would hang it well below the button.
            rect.sizeDelta = new Vector2(size * 1.6f, button.caption.fontSize + 8f);
            return button;
        }

        private void Copy(float size)
        {
            ActiveAbilityButton source = NativeWidgets.RoundButtonTemplate;
            // Keep the visual (and the tooltip's Localize) just long enough to read its references into the copy.
            GameObject copy = NativeWidgets.GraphicsCopy(source.gameObject, null, typeof(MaskedSpriteAnimated), typeof(ActiveAbilityButtonVisual), typeof(Localize));
            var visual = copy.GetComponent<ActiveAbilityButtonVisual>();
            var fields = Traverse.Create(visual);
            anim = fields.Field<MaskedSpriteAnimated>("maskedSpriteAnimated").Value;
            icon = fields.Field<Image>("iconImage").Value;
            tooltip = fields.Field<GameObject>("toolTip").Value;
            var title = fields.Field<Localize>("tooltipTitle").Value;
            tooltipText = title ? title.GetComponent<Text>() : null;
            var banned = fields.Field<GameObject>("bannedIndicator").Value;
            var charges = fields.Field<RectTransform>("chargesRemainBubble").Value;
            bubbleText = fields.Field<Text>("chargesRemainText").Value;
            UnityEngine.Object.DestroyImmediate(visual);
            // With the visual gone, scripts it required (the ability button itself) can go too.
            NativeWidgets.Strip(copy, typeof(MaskedSpriteAnimated));
            disc = anim ? anim.maskedSprite : null;
            bubble = charges ? charges.gameObject : null;
            if (banned) banned.SetActive(false);
            if (bubble) bubble.SetActive(false);
            if (tooltip)
            {
                tooltip.transform.localScale = Vector3.one;
                tooltip.SetActive(false);
            }
            if (anim) { anim.state = UIState.None; anim.selected = false; anim.avaliable = true; }
            if (disc) disc.radial = 1f;
            copy.transform.SetParent(root.transform, false);
            copy.SetActive(true);
            NativeWidgets.Fit((RectTransform)copy.transform, disc ? (RectTransform)disc.transform : null, size, 100f);
        }

        private void Fallback()
        {
            var go = new GameObject("Disc", typeof(RectTransform));
            go.transform.SetParent(root.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            fallbackDisc = go.AddComponent<Image>();
            fallbackDisc.color = new Color(0f, 0f, 0f, 0.45f);
            fallbackDisc.raycastTarget = false;
            var iconObject = new GameObject("Icon", typeof(RectTransform));
            iconObject.transform.SetParent(go.transform, false);
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.anchorMin = new Vector2(0.12f, 0.12f); iconRect.anchorMax = new Vector2(0.88f, 0.88f);
            iconRect.offsetMin = iconRect.offsetMax = Vector2.zero;
            icon = iconObject.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
        }

        public void SetIcon(Sprite sprite)
        {
            if (!icon || icon.sprite == sprite) return;
            icon.sprite = sprite;
            icon.enabled = sprite;
            icon.preserveAspect = true;
            icon.color = Color.white;
        }

        /// <summary>The charge bubble (native "x2" style); hidden when empty.</summary>
        public void SetCount(string text)
        {
            bool show = !string.IsNullOrEmpty(text);
            if (bubble)
            {
                if (bubble.activeSelf != show) bubble.SetActive(show);
                if (show && bubbleText) bubbleText.text = text;
            }
            else if (show && caption) caption.text = text;
        }

        public void SetTooltip(string text)
        {
            tooltipValue = text;
            if (tooltipText) tooltipText.text = text;
        }

        public void SetActive(bool active)
        {
            if (root.activeSelf != active) root.SetActive(active);
        }

        internal void Pointer(UIState state)
        {
            if (anim) anim.state = state;
            bool hover = state != UIState.None;
            if (tooltip && !string.IsNullOrEmpty(tooltipValue) && tooltip.activeSelf != hover) tooltip.SetActive(hover);
        }
    }

    /// <summary>Pointer handling for a <see cref="NativeRoundButton"/>: native hover and press looks, any mouse button.</summary>
    internal sealed class RoundButtonInput : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public NativeRoundButton owner;
        public Action<PointerEventData.InputButton> onClick;
        private bool inside;

        public void OnPointerEnter(PointerEventData eventData)
        {
            inside = true;
            owner.Pointer(UIState.Hover);
            FabricWrapper.PostEvent(FabricID.uiHover);
        }
        public void OnPointerExit(PointerEventData eventData) { inside = false; owner.Pointer(UIState.None); }
        public void OnPointerDown(PointerEventData eventData) => owner.Pointer(UIState.PointerButtonDown);
        public void OnPointerUp(PointerEventData eventData) => owner.Pointer(inside ? UIState.Hover : UIState.None);
        public void OnPointerClick(PointerEventData eventData) => onClick?.Invoke(eventData.button);
        private void OnDisable() { inside = false; owner?.Pointer(UIState.None); }
    }
}
