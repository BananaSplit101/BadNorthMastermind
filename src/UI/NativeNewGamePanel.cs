using System;
using System.Collections.Generic;
using HarmonyLib;
using I2.Loc;
using RTM.UISystem;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    /// <summary>
    /// The middle of the game's own New Game screen (difficulty, Allow Level Restarts, Begin), copied for the multiplayer
    /// draft: the difficulty without its arrows and the checkbox read-only, both showing what the mastermind chose; no
    /// Tutorial; Begin becomes the draft's own button (Ready, or Back for the host). Null if the game's screen can't be found.
    /// </summary>
    internal sealed class NativeNewGamePanel
    {
        public GameObject root;
        private Text difficulty;
        private GameObject checkmark;
        private Text buttonLabel;
        private Button button;
        private Action onClick;
        private int shownDifficulty = -1;

        public static NativeNewGamePanel Create(Transform parent, float scale)
        {
            NewGameOptionsPopup popup = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<NewGameOptionsPopup>())
                if (candidate && candidate.gameObject.scene.IsValid()) { popup = candidate; break; }
            if (!popup) return Fail("the game's New Game screen isn't loaded");
            var traverse = Traverse.Create(popup);
            var settings = traverse.Field<RectTransform>("settingsContainer").Value;
            var buttons = traverse.Field<RectTransform>("buttonContainer").Value;
            var difficultyWidget = traverse.Field<MultiSelectWidget>("difficultySetting").Value;
            if (!settings || !buttons || !difficultyWidget) return Fail("its settings aren't set up yet");
            // The panel: the smallest part of the screen holding both the settings and the Begin button.
            Transform panel = settings;
            while (panel && !buttons.IsChildOf(panel)) panel = panel.parent;
            if (!panel) return Fail("its settings and button aren't in one panel");

            // What to change, found on the original (the copy loses the scripts that know).
            var decorator = difficultyWidget.GetComponentInChildren<MultiSelectWidget_Decorator>(true);
            var decorated = decorator ? Traverse.Create(decorator) : null;
            string valuePath = Path(panel, decorated?.Field<RectTransform>("valueReference").Value);
            var arrowPaths = new List<string>
            {
                Path(panel, decorated?.Field<UIClickable>("decrementClick").Value?.transform),
                Path(panel, decorated?.Field<UIClickable>("incrementClick").Value?.transform),
            };
            var toggles = new List<BoolWidget>(settings.GetComponentsInChildren<BoolWidget>(true));
            toggles.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
            // Made in this order by the game: Allow Level Restarts, then Tutorial.
            string checkPath = null, tutorialPath = null, restartsLabelPath = null;
            if (toggles.Count > 0)
            {
                var box = toggles[0].GetComponentInChildren<BoolWidget_CheckboxDecorator>(true);
                checkPath = Path(panel, box ? Traverse.Create(box).Field<Image>("checkmark").Value?.transform : null);
                restartsLabelPath = Path(panel, toggles[0].label ? toggles[0].label.transform : null);
            }
            if (toggles.Count > 1) tutorialPath = Path(panel, toggles[1].transform);
            var begin = buttons.GetComponentInChildren<ButtonWidget>(true);
            string beginPath = Path(panel, begin ? begin.transform : null), labelPath = Path(panel, begin && begin.label ? begin.label.transform : null);
            if (valuePath == null || beginPath == null || labelPath == null) return Fail("its difficulty or Begin button wasn't found");

            // Copied inactive and stripped of scripts before it's shown (the game's would wake up and register themselves).
            var clone = NativeWidgets.GraphicsCopy(panel.gameObject, null);
            clone.SetActive(true);
            var result = new NativeNewGamePanel();
            Transform Find(string path)
            {
                if (path == null) return null;
                Transform at = clone.transform;
                if (path.Length > 0)
                    foreach (string step in path.Split('/'))
                    {
                        int index = int.Parse(step);
                        if (index >= at.childCount) return null;
                        at = at.GetChild(index);
                    }
                return at;
            }
            var arrows = new List<Transform>();
            foreach (string path in arrowPaths) arrows.Add(Find(path));
            Transform tutorial = Find(tutorialPath), value = Find(valuePath), check = Find(checkPath), beginCopy = Find(beginPath);
            result.buttonLabel = Find(labelPath)?.GetComponent<Text>();
            // Its text came from the game's translations, which the copy no longer has: set in case it was never shown.
            var restartsLabel = Find(restartsLabelPath)?.GetComponent<Text>();
            if (restartsLabel) restartsLabel.text = Translate("SETTINGS/ALLOW_REPLAY_LEVEL", "Allow Level Restarts");
            foreach (Transform arrow in arrows) if (arrow) arrow.gameObject.SetActive(false);
            bool hadTutorial = tutorial;
            if (tutorial) UnityEngine.Object.DestroyImmediate(tutorial.gameObject);
            if (value)
            {
                // The game shows one copy of the value text per difficulty, sliding between them: one, ours, is enough.
                foreach (Transform sibling in value.parent)
                    if (sibling != value && sibling.GetComponent<Text>()) sibling.gameObject.SetActive(false);
                value.gameObject.SetActive(true);
                result.difficulty = value.GetComponent<Text>();
            }
            if (check) result.checkmark = check.gameObject;
            foreach (var group in clone.GetComponentsInChildren<CanvasGroup>(true)) group.blocksRaycasts = true;
            // A nested canvas takes clicks only through its own raycaster.
            foreach (var canvas in clone.GetComponentsInChildren<Canvas>(true))
                if (!canvas.GetComponent<GraphicRaycaster>()) canvas.gameObject.AddComponent<GraphicRaycaster>();
            if (beginCopy)
            {
                var target = beginCopy.GetComponent<Graphic>() ?? beginCopy.GetComponentInChildren<Image>(true);
                if (target) target.raycastTarget = true;
                result.button = beginCopy.gameObject.AddComponent<Button>();
                result.button.targetGraphic = target;
                result.button.onClick.AddListener(() =>
                {
                    FabricWrapper.PostEvent(FabricID.uiButtonClick);
                    result.onClick?.Invoke();
                });
            }
            if (result.difficulty == null || result.button == null) { UnityEngine.Object.Destroy(clone); return Fail("its copy came out incomplete"); }

            var holder = new GameObject("Campaign Settings", typeof(RectTransform));
            holder.transform.SetParent(parent, false);
            Vector2 size = ((RectTransform)panel).rect.size;
            var element = holder.AddComponent<LayoutElement>();
            element.preferredWidth = size.x * scale; element.preferredHeight = size.y * scale;
            clone.transform.SetParent(holder.transform, false);
            var rect = (RectTransform)clone.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size; rect.anchoredPosition = Vector2.zero;
            rect.localRotation = Quaternion.identity; rect.localScale = Vector3.one * scale;
            result.root = holder;
            if (!reported)
            {
                reported = true;
                ModLog.Info($"Commander draft: the game's New Game panel, {size.x:0}x{size.y:0} at scale {scale:0.00}" +
                    $" (arrows {arrows.FindAll(a => a).Count}/2, checkbox {(check ? "found" : "missing")}, tutorial {(hadTutorial ? "removed" : "not found")})");
            }
            return result;
        }

        /// <summary>The mastermind's choices, as the game would show them.</summary>
        public void Show(int level, bool allowReplays)
        {
            if (checkmark && checkmark.activeSelf != allowReplays) checkmark.SetActive(allowReplays);
            if (level == shownDifficulty || !difficulty) return;
            shownDifficulty = level;
            difficulty.text = DifficultyName(level);
        }

        /// <summary>The button under the settings: its label, whether it can be pressed, and what it does.</summary>
        public void SetButton(string label, bool interactable, Action action)
        {
            onClick = action;
            if (buttonLabel && buttonLabel.text != label) buttonLabel.text = label;
            if (button && button.interactable != interactable) button.interactable = interactable;
            if (button && button.gameObject.activeSelf != (action != null)) button.gameObject.SetActive(action != null);
        }

        public static string DifficultyName(int level)
        {
            var value = (Difficulty)level;
            string fallback = value == Difficulty.VeryHard ? "Very Hard" : value.ToString();
            try { return Translate(value.GetLocTerm(), fallback); }
            catch (NotImplementedException) { return fallback; }
        }

        private static string Translate(string term, string fallback)
        {
            try
            {
                string text = LocalizationManager.GetTranslation(term);
                if (!string.IsNullOrEmpty(text)) return text;
            }
            catch (Exception) { }
            return fallback;
        }

        /// <summary>
        /// A child's place below <paramref name="root"/> as sibling indices ("2/0/1"; "" for the root itself, null if it
        /// isn't below it): names repeat in the game's widgets, positions don't.
        /// </summary>
        private static string Path(Transform root, Transform child)
        {
            if (!child || !child.IsChildOf(root)) return null;
            var parts = new List<string>();
            for (Transform current = child; current != root; current = current.parent) parts.Add(current.GetSiblingIndex().ToString());
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static bool warned, reported;

        private static NativeNewGamePanel Fail(string why)
        {
            if (!warned) { warned = true; ModLog.Warn("Commander draft: the game's New Game panel can't be copied (" + why + "); using a plain one"); }
            return null;
        }
    }
}
