using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.ScriptAnimations;

namespace BadNorthMP.UI
{
    /// <summary>
    /// A cloned commander card's arrow tabs, animated as the game's NewGameTabAnimator does (the clone loses it): each
    /// tab peeks out, slides all the way out while the card is under the mouse, and swells a touch under the mouse.
    /// </summary>
    internal sealed class CardTabs : MonoBehaviour
    {
        private sealed class Tab
        {
            public RectTransform rect, expand;
            public Vector2 peek, expansion;
            public readonly List<Transform> images = new List<Transform>();
            public float hover, down;
        }

        private static readonly System.Type AnimatorType = AccessTools.TypeByName("Voxels.TowerDefense.UI.NewGameTabAnimator");
        private const string SlideSound = "UI/Menu/NewGameTabs";

        private readonly List<Tab> tabs = new List<Tab>();
        private RectTransform card;
        private Canvas canvas;
        private float expanded;
        private bool open;

        /// <summary>
        /// Before cloning: each arrow tab's slid-out size on the game's card, by its path in the card. The animator keeps
        /// it only in a closure, so the tab is slid out for a moment and back (a card never set up is still that size).
        /// </summary>
        public static Dictionary<string, Vector2> SlidOutSizes(Transform original)
        {
            var sizes = new Dictionary<string, Vector2>();
            if (AnimatorType == null) return sizes;
            foreach (Component tab in original.GetComponentsInChildren(AnimatorType, true))
            {
                var expand = Traverse.Create(tab).Field<RectTransform>("expandTransform").Value;
                if (!expand) continue;
                var visible = Traverse.Create(tab).Field<AnimatedState>("visible").Value;
                var expanded = Traverse.Create(tab).Field<AnimatedState>("expanded").Value;
                Vector2 size;
                if (visible == null || expanded == null) size = expand.rect.size;
                else
                {
                    float wasVisible = visible.anim.current, wasExpanded = expanded.anim.current;
                    visible.anim.SetCurrent(1f);
                    expanded.anim.SetCurrent(1f);
                    size = expand.rect.size;
                    visible.anim.SetCurrent(wasVisible);
                    expanded.anim.SetCurrent(wasExpanded);
                }
                sizes[PathOf(original, tab.transform)] = size;
            }
            return sizes;
        }

        /// <summary>
        /// After cloning, before the clone's scripts are stripped: animates its tabs from <paramref name="host"/>
        /// (outside the clone, so the stripping leaves it be).
        /// </summary>
        public static void Attach(GameObject host, GameObject clone, Dictionary<string, Vector2> slidOut)
        {
            if (AnimatorType == null) return;
            var tabs = new List<Tab>();
            foreach (Component animator in clone.GetComponentsInChildren(AnimatorType, true))
            {
                var traverse = Traverse.Create(animator);
                var expand = traverse.Field<RectTransform>("expandTransform").Value;
                if (!expand) continue;
                var tab = new Tab { rect = (RectTransform)animator.transform, expand = expand, peek = traverse.Field<Vector2>("peekSize").Value };
                tab.expansion = slidOut.TryGetValue(PathOf(clone.transform, animator.transform), out Vector2 size) ? size - tab.rect.rect.size : tab.peek;
                foreach (string field in new[] { "mainImage", "secondaryImage" })
                {
                    var image = traverse.Field<Component>(field).Value;
                    if (image) tab.images.Add(image.transform);
                }
                tabs.Add(tab);
            }
            if (tabs.Count == 0) return;
            var animated = host.AddComponent<CardTabs>();
            animated.tabs.AddRange(tabs);
            animated.card = (RectTransform)clone.transform;
        }

        private static string PathOf(Transform root, Transform child)
        {
            var parts = new List<string>();
            for (Transform current = child; current && current != root; current = current.parent) parts.Add(current.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private void OnEnable()
        {
            expanded = 0f;
            open = false;
            Apply();
        }

        private void Update()
        {
            if (!canvas) canvas = GetComponentInParent<Canvas>();
            Camera eye = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 mouse = Input.mousePosition;
            // As the game's: out while the card is under the mouse (always, playing by touch).
            bool want = (Profile.userSettings != null && Profile.userSettings.cursorBehaviour == UserSettings.CursorBehaviour.Touch) ||
                (card && RectTransformUtility.RectangleContainsScreenPoint(card, mouse, eye));
            if (want != open)
            {
                open = want;
                FabricWrapper.PostEvent(SlideSound);
            }
            float dt = Time.unscaledDeltaTime;
            expanded = Toward(expanded, open ? 1f : 0f, dt);
            foreach (Tab tab in tabs)
            {
                bool over = tab.expand && RectTransformUtility.RectangleContainsScreenPoint(tab.expand, mouse, eye);
                tab.hover = Toward(tab.hover, over ? 1f : 0f, dt);
                tab.down = Toward(tab.down, over && Input.GetMouseButton(0) ? 1f : 0f, dt);
            }
            Apply();
        }

        private void Apply()
        {
            foreach (Tab tab in tabs)
            {
                if (!tab.rect || !tab.expand) continue;
                Vector2 grow = Vector2.Lerp(tab.peek, tab.expansion, expanded);
                tab.expand.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, tab.rect.rect.width + grow.x);
                tab.expand.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, tab.rect.rect.height + grow.y);
                float swell = Mathf.Min(1f + tab.hover * 0.2f - tab.down * 0.1f, 1.2f);
                foreach (Transform image in tab.images)
                    if (image)
                    {
                        Vector3 scale = image.localScale;
                        image.localScale = new Vector3(scale.x, scale.y, swell);
                    }
            }
        }

        /// <summary>The game's LerpTowards(16, 3): most of the way quickly, the last bit at a steady pace.</summary>
        private static float Toward(float current, float target, float dt) =>
            Mathf.MoveTowards(current, target, Mathf.Max(Mathf.Abs(target - current) * 16f, 3f) * dt);
    }
}
