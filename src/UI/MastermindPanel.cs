using System;
using System.Collections.Generic;
using BadNorthMP.Game;
using BadNorthMP.Net;
using BadNorthMP.Spectator;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    /// <summary>
    /// The mastermind's battle panel: gold, the army builder (unit counts, beach, launch) and give up.
    /// Plain uGUI on its own canvas, styled with the game's font and button sprite.
    /// </summary>
    internal class MastermindPanel : MonoBehaviour
    {
        private const float Width = 400f;
        private static readonly Color PanelColor = new Color(0.16f, 0.15f, 0.15f, 0.92f);
        private static readonly Color ButtonColor = new Color(0.98f, 0.92f, 0.85f, 1f);
        private static readonly Color DarkText = new Color(0.2f, 0.17f, 0.15f, 1f);
        private static readonly Color LightText = new Color(0.98f, 0.94f, 0.88f, 1f);
        private static readonly Color Warning = new Color(1f, 0.55f, 0.45f, 1f);

        private Font font;
        private Sprite buttonSprite;
        private GameObject canvasObject;
        private GameObject panel;
        private Text goldText, incomeText, totalText, beachText, statusText;
        private Button launchButton, giveUpButton;
        private Text giveUpLabel;
        private float giveUpConfirmUntil;
        private Island builtFor;
        private readonly List<UnitRow> rows = new List<UnitRow>();
        private int beachIndex;
        private LineRenderer beachMarker;

        private class UnitRow
        {
            public VikingReference type;
            public int count;
            public Text countText;
        }

        private static string Name(VikingAgent.Type type)
        {
            switch (type)
            {
                case VikingAgent.Type.Sword: return "Swordsman";
                case VikingAgent.Type.SwordShield: return "Shieldbearer";
                case VikingAgent.Type.Archer: return "Archer";
                case VikingAgent.Type.Tank: return "Brute";
                case VikingAgent.Type.Beserker: return "Berserker";
                case VikingAgent.Type.AxeThrower: return "Axe thrower";
                case VikingAgent.Type.TankArcher: return "Brute archer";
                case VikingAgent.Type.Twohanded: return "Champion";
                default: return type.ToString();
            }
        }

        private void Update()
        {
            // Also shown (as a preview) when replaying a battle solo with F11.
            bool show = SpectatorSession.Active && SpectatorSession.IslandReady()
                && (Roles.IsMastermind || !SteamSession.Instance.InLobby);
            if (!show)
            {
                if (canvasObject && canvasObject.activeSelf)
                    canvasObject.SetActive(false);
                if (beachMarker)
                    beachMarker.gameObject.SetActive(false);
                return;
            }

            Island island = GameAccess.CurrentIsland;
            if (!canvasObject)
                BuildCanvas();
            if (builtFor != island)
                BuildContents(island);
            if (!canvasObject.activeSelf)
                canvasObject.SetActive(true);
            Refresh(island);
        }

        // ---- Building ------------------------------------------------------------------------

        private void FindGameStyle()
        {
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            foreach (MainMenu menu in Resources.FindObjectsOfTypeAll<MainMenu>())
            {
                var prefab = Traverse.Create(menu).Field<ButtonWidget>("textButtonPrefab").Value;
                if (!prefab)
                    continue;
                if (prefab.label && prefab.label.font)
                    font = prefab.label.font;
                foreach (Image image in prefab.GetComponentsInChildren<Image>(true))
                {
                    if (image.sprite)
                    {
                        buttonSprite = image.sprite;
                        break;
                    }
                }
                break;
            }
        }

        private void BuildCanvas()
        {
            FindGameStyle();
            canvasObject = new GameObject("BNMP Mastermind Canvas");
            DontDestroyOnLoad(canvasObject);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            canvasObject.AddComponent<GraphicRaycaster>();

            panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasObject.transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-20f, 0f);
            rect.sizeDelta = new Vector2(Width, 0f);
            var background = panel.AddComponent<Image>();
            background.sprite = buttonSprite;
            background.type = buttonSprite ? Image.Type.Sliced : Image.Type.Simple;
            background.color = PanelColor;
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 16, 16);
            layout.spacing = 8f;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void BuildContents(Island island)
        {
            builtFor = island;
            foreach (Transform child in panel.transform)
                Destroy(child.gameObject);
            rows.Clear();
            beachIndex = 0;

            Label(panel.transform, "Mastermind", 34, LightText, TextAnchor.MiddleCenter);
            goldText = Label(panel.transform, "", 30, new Color(1f, 0.85f, 0.4f), TextAnchor.MiddleCenter);
            incomeText = Label(panel.transform, "", 19, LightText, TextAnchor.MiddleCenter);

            Label(panel.transform, "Army", 24, LightText, TextAnchor.MiddleLeft);
            foreach (VikingReference type in Economy.AvailableTypes(island))
                rows.Add(UnitRowFor(type));

            Transform beachRow = Row(panel.transform, 46f);
            Button(beachRow, "<", 46f, () => StepBeach(-1));
            beachText = Label(beachRow, "", 22, LightText, TextAnchor.MiddleCenter, flexible: true);
            Button(beachRow, ">", 46f, () => StepBeach(1));

            totalText = Label(panel.transform, "", 21, LightText, TextAnchor.MiddleCenter);
            launchButton = Button(panel.transform, "Launch wave", 0f, Launch, height: 58f);
            giveUpButton = Button(panel.transform, "Give up island", 0f, GiveUp, height: 40f);
            giveUpLabel = giveUpButton.GetComponentInChildren<Text>();
            statusText = Label(panel.transform, "", 19, LightText, TextAnchor.MiddleCenter);
        }

        private UnitRow UnitRowFor(VikingReference type)
        {
            var row = new UnitRow { type = type };
            Transform line = Row(panel.transform, 52f);

            var iconObject = new GameObject("Icon", typeof(RectTransform));
            iconObject.transform.SetParent(line, false);
            var icon = iconObject.AddComponent<Image>();
            icon.sprite = type.sprite2;
            icon.preserveAspect = true;
            icon.enabled = type.sprite2;
            var iconLayout = iconObject.AddComponent<LayoutElement>();
            iconLayout.preferredWidth = iconLayout.preferredHeight = 48f;

            Label(line, $"{Name(type.type)}  <color=#FFD966>{Economy.UnitCost(type)}g</color>", 22, LightText, TextAnchor.MiddleLeft, flexible: true);
            Button(line, "-", 44f, () => row.count = Mathf.Max(0, row.count - 1));
            row.countText = Label(line, "0", 24, LightText, TextAnchor.MiddleCenter);
            row.countText.GetComponent<LayoutElement>().preferredWidth = 40f;
            Button(line, "+", 44f, () => row.count = Mathf.Min(99, row.count + 1));
            return row;
        }

        private Transform Row(Transform parent, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childForceExpandWidth = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childAlignment = TextAnchor.MiddleCenter;
            go.AddComponent<LayoutElement>().preferredHeight = height;
            return go.transform;
        }

        private Text Label(Transform parent, string text, int size, Color color, TextAnchor anchor, bool flexible = false)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.alignment = anchor;
            label.supportRichText = true;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = text;
            var layout = go.AddComponent<LayoutElement>();
            if (flexible)
                layout.flexibleWidth = 1f;
            return label;
        }

        private Button Button(Transform parent, string text, float width, Action onClick, float height = 44f)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = buttonSprite;
            image.type = buttonSprite ? Image.Type.Sliced : Image.Type.Simple;
            image.color = ButtonColor;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                FabricWrapper.PostEvent(FabricID.uiButtonClick);
                onClick();
            });
            var layout = go.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            if (width > 0f)
                layout.preferredWidth = width;

            var labelObject = new GameObject("Text", typeof(RectTransform));
            labelObject.transform.SetParent(go.transform, false);
            var rect = (RectTransform)labelObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var label = labelObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = 24;
            label.color = DarkText;
            label.alignment = TextAnchor.MiddleCenter;
            label.text = text;
            return button;
        }

        // ---- Behaviour -----------------------------------------------------------------------

        private void StepBeach(int step)
        {
            int count = BeachChoices.Get(builtFor).Count;
            if (count > 0)
                beachIndex = (beachIndex + step + count) % count;
        }

        private void Launch()
        {
            var units = new List<KeyValuePair<VikingAgent.Type, int>>();
            foreach (UnitRow row in rows)
            {
                if (row.count > 0)
                    units.Add(new KeyValuePair<VikingAgent.Type, int>(row.type.type, row.count));
            }
            if (units.Count > 0)
                MastermindNet.RequestWave(beachIndex, units);
        }

        private void GiveUp()
        {
            if (Time.unscaledTime < giveUpConfirmUntil)
            {
                giveUpConfirmUntil = 0f;
                MastermindNet.RequestGiveUp();
                return;
            }
            giveUpConfirmUntil = Time.unscaledTime + 3f;
        }

        private void Refresh(Island island)
        {
            MasterState s = MastermindNet.State;
            bool live = MastermindNet.HasState && s.inBattle;
            goldText.text = live ? $"{Mathf.FloorToInt(s.gold)} gold" : "Waiting for the battle...";
            incomeText.text = !live ? ""
                : s.incomeLeft > 0.5f ? $"+{s.incomePerSecond:0.0}/s  ({s.incomeLeft:0} more this island)"
                : s.done ? "Out of gold: finish them off!" : "No more income this island";

            int total = 0, unitCount = 0;
            foreach (UnitRow row in rows)
            {
                row.countText.text = row.count.ToString();
                total += row.count * Economy.UnitCost(row.type);
                unitCount += row.count;
            }
            bool affordable = live && total <= s.gold + 0.001f;
            totalText.text = unitCount == 0 ? "Choose your Vikings" : $"{unitCount} Vikings: {total} gold";
            totalText.color = unitCount > 0 && !affordable ? Warning : LightText;
            launchButton.interactable = live && unitCount > 0 && affordable;
            giveUpButton.interactable = live && !s.done;
            giveUpLabel.text = Time.unscaledTime < giveUpConfirmUntil ? "Really give up?" : "Give up island";

            List<BeachChoice> beaches = BeachChoices.Get(island);
            if (beaches.Count > 0)
            {
                beachIndex = Mathf.Clamp(beachIndex, 0, beaches.Count - 1);
                beachText.text = $"Land at the {beaches[beachIndex].label} beach";
                ShowMarker(beaches[beachIndex].center);
            }
            else
            {
                beachText.text = "No beaches";
            }

            bool recent = Time.unscaledTime - MastermindNet.LastResultTime < 6f;
            statusText.text = recent ? MastermindNet.LastResult : "";
            statusText.color = MastermindNet.LastResult.StartsWith("Can't") ? Warning : LightText;
        }

        /// <summary>A red ring on the island around the chosen beach.</summary>
        private void ShowMarker(Vector3 center)
        {
            if (!beachMarker)
            {
                var go = new GameObject("BNMP Beach Marker");
                DontDestroyOnLoad(go);
                beachMarker = go.AddComponent<LineRenderer>();
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                beachMarker.material = new Material(shader) { color = new Color(0.9f, 0.15f, 0.1f, 0.9f) };
                beachMarker.startColor = beachMarker.endColor = new Color(0.9f, 0.15f, 0.1f, 0.9f);
                beachMarker.startWidth = beachMarker.endWidth = 0.05f;
                beachMarker.loop = true;
                beachMarker.useWorldSpace = true;
                beachMarker.positionCount = 32;
            }
            beachMarker.gameObject.SetActive(true);
            float pulse = 0.45f + Mathf.Sin(Time.unscaledTime * 4f) * 0.05f;
            for (int i = 0; i < 32; i++)
            {
                float a = i / 32f * Mathf.PI * 2f;
                beachMarker.SetPosition(i, center + new Vector3(Mathf.Cos(a) * pulse, 0.06f, Mathf.Sin(a) * pulse));
            }
        }
    }
}
