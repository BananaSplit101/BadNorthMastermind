using System;
using System.Collections.Generic;
using BadNorthMP.Game;
using BadNorthMP.Net;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    /// <summary>
    /// The mastermind's war chest over a darkened screen: a round button for every Viking type along the top (types
    /// not seen in this campaign yet are greyed out under a lock), and for the picked type its three upgrades (level
    /// pips, bonus, price) beside recruiting a commander of that type (each one owned makes the next dearer). Below:
    /// the commanders as small buttons (an hourglass on those resting, a skull on those that fell on this island);
    /// click one to sell it back. Then the leader's latest reply, Give up (in a fight), Sell and Close. Built from the game's round buttons, coin and text. Opened from the
    /// battle screen's coin, the map's coin or the map's Upgrades button.
    /// </summary>
    internal static class WarChestPopup
    {
        private static readonly Color Gold = new Color(1f, 0.83f, 0.42f, 1f);
        private static readonly Color Warning = new Color(1f, 0.62f, 0.5f, 1f);
        private static readonly Color Muted = new Color(1f, 1f, 1f, 0.62f);
        private static readonly Color EmptyPip = new Color(1f, 1f, 1f, 0.22f);
        private static readonly UnitUpgrade[] Kinds = { UnitUpgrade.Health, UnitUpgrade.Damage, UnitUpgrade.Count };
        private static readonly string[] KindNames = { "Health", "Damage", "Crew" };
        private static readonly string[] KindBonus = { "health", "damage", "more Vikings" };

        private sealed class ChestButton
        {
            public TextButton input;
            public Text label;
            public GameObject coin;
        }

        private static GameObject root;
        private static Font font;
        private static bool fighting, builtNative;
        private static int picked = -1;
        private static float giveUpConfirmUntil, sellConfirmUntil, nextReferenceSearch;
        // The commander picked in the roster row (for selling), or -1.
        private static int pickedCommander = -1;
        private static Text goldText, nameText, hintText, resultText, recruitTitle, recruitCrew, recruitCount;
        private static ChestButton recruitButton, giveUpButton, sellButton, closeButton;
        private static readonly NativeRoundButton[] units = new NativeRoundButton[MastermindUpgrades.TypeCount];
        private static readonly GameObject[] locks = new GameObject[MastermindUpgrades.TypeCount];
        private static readonly Image[,] pips = new Image[3, MastermindUpgrades.MaxLevel];
        private static readonly Text[] bonusTexts = new Text[3];
        private static readonly ChestButton[] buyButtons = new ChestButton[3];
        private static readonly NativeRoundButton[] commanders = new NativeRoundButton[EconomyConfig.MaxCommanders];
        private static readonly GameObject[] resting = new GameObject[EconomyConfig.MaxCommanders];
        private static readonly GameObject[] fallen = new GameObject[EconomyConfig.MaxCommanders];
        private static readonly Dictionary<int, VikingReference> references = new Dictionary<int, VikingReference>();

        public static bool IsOpen => root && root.activeSelf;

        public static void Open(bool battle)
        {
            if (IsOpen) return;
            fighting = battle;
            giveUpConfirmUntil = sellConfirmUntil = 0f;
            pickedCommander = -1;
            // Built on first use, and again once the game's round buttons have loaded (they come with the island UI).
            if (!root || (!builtNative && NativeWidgets.HasRoundButton)) Build();
            root.SetActive(true); // the button that opened it already clicked
            Show();
        }

        public static void Close()
        {
            if (IsOpen) root.SetActive(false);
        }

        /// <summary>Called every frame while the mastermind screen is up.</summary>
        public static void Refresh(bool battle)
        {
            if (!IsOpen) return;
            fighting = battle;
            Show();
        }

        private static MasterUpgradeState State => MastermindNet.HasFreshState ? MastermindNet.State.upgrades : null;

        private static bool Unlocked(MasterUpgradeState state, int type) => state != null && state.unitCosts[type] > 0;

        private static string Name(int type) => VikingCommanders.TypeName(type);

        private static string Plural(int type)
        {
            string name = Name(type);
            return name.EndsWith("man") ? name.Substring(0, name.Length - 3) + "men" : name + "s";
        }

        // ---- Showing the state ---------------------------------------------------------------------

        private static void Show()
        {
            var state = State;
            if (picked < 0 && state != null)
                for (int i = 0; i < MastermindUpgrades.TypeCount && picked < 0; i++) if (Unlocked(state, i)) picked = i;
            int type = Mathf.Max(0, picked);
            bool open = Unlocked(state, type);
            goldText.text = state != null ? Mathf.FloorToInt(state.gold).ToString() : "-";

            for (int i = 0; i < units.Length; i++)
            {
                bool unlocked = Unlocked(state, i);
                VikingReference reference = Reference(i);
                units[i].SetIcon(reference ? reference.sprite2 : null);
                units[i].Available = unlocked;
                units[i].Selected = i == type;
                units[i].caption.color = i == type ? Gold : unlocked ? Color.white : Muted;
                units[i].SetTooltip(unlocked ? Name(i) : Name(i) + ": locked");
                if (locks[i].activeSelf == unlocked) locks[i].SetActive(!unlocked);
            }

            nameText.text = Name(type) + "  <size=22><color=#FFFFFFA0>" +
                (open ? state.unitCosts[type] + " gold each" : "locked") + "</color></size>";
            bool busy = MastermindNet.UpgradePending;
            for (int k = 0; k < Kinds.Length; k++)
            {
                int level = state != null ? state.Level((VikingAgent.Type)type, Kinds[k]) : 0;
                for (int j = 0; j < MastermindUpgrades.MaxLevel; j++) pips[k, j].color = j < level ? Gold : EmptyPip;
                bonusTexts[k].text = $"+{25 * level}% {KindBonus[k]}";
                ChestButton buy = buyButtons[k];
                if (!open) Label(buy, "Locked", false, false, Muted);
                else if (level >= MastermindUpgrades.MaxLevel) Label(buy, "Maxed", false, false, Gold);
                else
                {
                    int cost = MastermindUpgrades.Cost(state.unitCosts[type], Kinds[k], level);
                    Label(buy, cost.ToString(), true, state.canBuy && !busy, cost <= state.gold + 0.001f ? Color.white : Warning);
                }
            }

            hintText.text = state == null ? "Waiting for the campaign leader..."
                : !open ? $"Not seen in this campaign yet: {Plural(type)} unlock once they appear on an island you've reached."
                : !state.canBuy ? "Upgrades can be bought on the campaign map or during a fight."
                : "Each level adds 25%. Upgrades last the whole campaign.";

            // Recruiting a commander of the picked type.
            int owned = state != null ? state.commanders.Count : 0;
            recruitTitle.text = Name(type) + " commander";
            recruitCrew.text = $"{CommanderRoster.SquadSize(type)} Vikings, random trait";
            if (!open) Label(recruitButton, "Locked", false, false, Muted);
            else if (owned >= EconomyConfig.MaxCommanders) Label(recruitButton, "Roster full", false, false, Muted);
            else
            {
                int price = CommanderRoster.Price(state.unitCosts[type], type, owned);
                Label(recruitButton, price.ToString(), true, state.canBuy && !busy, price <= state.gold + 0.001f ? Color.white : Warning);
            }
            int dearer = Mathf.RoundToInt(EconomyConfig.CommanderPriceStepPerOwned * owned * 100f);
            recruitCount.text = $"Commanders: {owned} of {EconomyConfig.MaxCommanders}" + (dearer > 0 ? $"  ·  next one +{dearer}%" : "");

            ShowCommanders(state);
            string result = MastermindNet.LastResult;
            bool recent = busy || Time.unscaledTime - MastermindNet.LastResultTime < 6f;
            resultText.text = recent && !string.IsNullOrEmpty(result) && !result.StartsWith("+") ? result : "";
            resultText.color = result != null && result.StartsWith("Can't") ? Warning : Gold;

            giveUpButton.input.gameObject.SetActive(fighting);
            bool confirming = Time.unscaledTime < giveUpConfirmUntil;
            Label(giveUpButton, confirming ? "Click again to give up" : "Give up this island", false, true, confirming ? Warning : Color.white);
            bool selling = ShowSell(state, busy);
            ((RectTransform)closeButton.input.transform).anchoredPosition = new Vector2(fighting || selling ? 390f : 0f, -390f);
        }

        /// <summary>Sell, for the commander picked in the roster row: "Sell [coin] 27", click again to confirm.</summary>
        private static bool ShowSell(MasterUpgradeState state, bool busy)
        {
            var commander = state != null ? state.commanders.Find(c => c.id == pickedCommander) : null;
            if (commander == null) pickedCommander = -1;
            bool show = commander != null;
            if (sellButton.input.gameObject.activeSelf != show) sellButton.input.gameObject.SetActive(show);
            if (!show) return false;
            bool onIsland = commander.status == CommanderStatus.Deployed;
            bool confirming = Time.unscaledTime < sellConfirmUntil;
            sellButton.input.extra.text = onIsland ? "On the island: can't sell" : confirming ? "Click again to sell for" : "Sell for";
            Label(sellButton, commander.sellValue.ToString(), !onIsland, !onIsland && state.canBuy && !busy, confirming ? Warning : Color.white);
            return true;
        }

        /// <summary>The commanders as small buttons with their number; resting ones greyed under an hourglass.</summary>
        private static void ShowCommanders(MasterUpgradeState state)
        {
            int count = state != null ? state.commanders.Count : 0;
            for (int i = 0; i < commanders.Length; i++)
            {
                NativeRoundButton button = commanders[i];
                button.SetActive(i < count);
                if (i >= count) continue;
                var commander = state.commanders[i];
                bool rests = commander.status == CommanderStatus.Resting;
                VikingReference reference = Reference(commander.type);
                button.SetIcon(reference ? reference.sprite2 : null);
                button.SetCount(commander.id.ToString());
                bool dead = commander.status == CommanderStatus.Deployed && MastermindPanel.Instance && MastermindPanel.Instance.IsFallen(commander.id);
                button.Available = !rests && !dead;
                button.Selected = commander.id == pickedCommander;
                button.SetTooltip(MastermindPanel.Instance ? MastermindPanel.Instance.CommanderCount(commander)
                    : VikingCommanders.Count(commander.crew, commander.type));
                if (resting[i].activeSelf != rests) resting[i].SetActive(rests);
                if (fallen[i].activeSelf != dead) fallen[i].SetActive(dead);
            }
        }

        private static void Label(ChestButton button, string text, bool coin, bool interactable, Color color)
        {
            if (button.label.text != text) button.label.text = text;
            if (button.coin && button.coin.activeSelf != coin) button.coin.SetActive(coin);
            button.input.normal = color;
            button.input.interactable = interactable;
            button.input.Paint();
        }

        /// <summary>A loaded reference with an icon for a Viking type (searched at most every two seconds).</summary>
        private static VikingReference Reference(int type)
        {
            if (references.TryGetValue(type, out VikingReference found) && found) return found;
            if (Time.unscaledTime < nextReferenceSearch) return null;
            nextReferenceSearch = Time.unscaledTime + 2f;
            foreach (VikingReference candidate in Resources.FindObjectsOfTypeAll<VikingReference>())
                if (candidate && candidate.sprite2 && MastermindUpgrades.ValidType(candidate.type))
                    references[(int)candidate.type] = candidate;
            return references.TryGetValue(type, out found) ? found : null;
        }

        // ---- Actions -------------------------------------------------------------------------------

        private static void Pick(int type)
        {
            if (picked == type) return;
            picked = type;
            FabricWrapper.PostEvent(Unlocked(State, type) ? "UI/InGame/UnitSelect" : "UI/InGame/Error");
        }

        private static void Buy(int kind)
        {
            if (picked >= 0) MastermindNet.RequestUpgrade((VikingAgent.Type)picked, Kinds[kind]);
        }

        private static void Recruit()
        {
            if (picked >= 0) MastermindNet.RequestCommanderBuy((VikingAgent.Type)picked);
        }

        /// <summary>A commander in the roster row: pick it (to sell), or put it down again.</summary>
        private static void PickCommander(int slot)
        {
            var state = State;
            if (state == null || slot >= state.commanders.Count) return;
            int id = state.commanders[slot].id;
            pickedCommander = pickedCommander == id ? -1 : id;
            sellConfirmUntil = 0f;
            FabricWrapper.PostEvent(pickedCommander >= 0 ? "UI/InGame/UnitSelect" : "UI/InGame/UnitDeselect");
        }

        private static void Sell()
        {
            if (pickedCommander < 0) return;
            if (Time.unscaledTime < sellConfirmUntil)
            {
                sellConfirmUntil = 0f;
                MastermindNet.RequestCommanderSell(pickedCommander);
                pickedCommander = -1;
                return;
            }
            sellConfirmUntil = Time.unscaledTime + 3f;
        }

        private static void GiveUp()
        {
            if (Time.unscaledTime < giveUpConfirmUntil)
            {
                giveUpConfirmUntil = 0f;
                MastermindNet.RequestGiveUp();
                Close();
                return;
            }
            giveUpConfirmUntil = Time.unscaledTime + 3f;
        }

        // ---- Building ------------------------------------------------------------------------------

        private static void Build()
        {
            if (root) UnityEngine.Object.Destroy(root);
            builtNative = NativeWidgets.HasRoundButton;
            font = GameFont();
            root = new GameObject("BNMP War Chest");
            UnityEngine.Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 520; // over the mastermind screen (500), under map notices (600)
            canvas.additionalShaderChannels = NativeWidgets.ShaderChannels;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();

            // The darkened screen takes every click; a click on it (not on the panel) closes the war chest.
            GameObject shade = Node(root.transform, "Shade");
            Stretch(shade);
            shade.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.68f);
            shade.AddComponent<ClickCatcher>().onClick = Close;
            GameObject panel = Node(root.transform, "Panel");
            Place(panel, Vector2.zero, new Vector2(1240f, 860f));
            panel.AddComponent<Image>().color = Color.clear; // clicks between the widgets stay on the panel

            // Top: the title and the war chest's gold.
            Text title = Label(panel, "War chest", 46, new Vector2(0f, 360f));
            title.alignment = TextAnchor.MiddleCenter;
            GameObject chest = Node(panel.transform, "Gold");
            Place(chest, new Vector2(500f, 360f), new Vector2(220f, 50f));
            var chestRow = chest.AddComponent<HorizontalLayoutGroup>();
            chestRow.spacing = 10f; chestRow.childAlignment = TextAnchor.MiddleRight;
            chestRow.childControlWidth = chestRow.childControlHeight = true;
            chestRow.childForceExpandWidth = chestRow.childForceExpandHeight = false;
            NativeWidgets.Coin(chest.transform, 42f);
            goldText = NativeWidgets.Text(chest.transform, font, "-", 36);
            Text subtitle = Label(panel, "Filled by burning houses, killing defenders and finishing islands. Spent on upgrades and commanders.", 20, new Vector2(0f, 312f));
            subtitle.color = Muted;

            // Every Viking type, locked ones greyed out under a lock.
            GameObject strip = Node(panel.transform, "Units");
            Place(strip, new Vector2(0f, 200f), new Vector2(1200f, 110f));
            var row = strip.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 34f; row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = false;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            for (int i = 0; i < units.Length; i++)
            {
                int type = i;
                NativeRoundButton unit = NativeRoundButton.Create(strip.transform, 100f, font, click =>
                {
                    if (click == PointerEventData.InputButton.Left) Pick(type);
                });
                unit.caption.fontSize = 19;
                unit.caption.text = Name(i);
                units[i] = unit;
                locks[i] = ModIcons.Badge(unit.root.transform, ModIcons.Padlock, 40f);
            }
            Image divider = Node(panel.transform, "Divider").AddComponent<Image>();
            divider.color = new Color(1f, 1f, 1f, 0.18f);
            divider.raycastTarget = false;
            Place(divider.gameObject, new Vector2(0f, 100f), new Vector2(1140f, 2f));

            // Left: the picked type's upgrades.
            nameText = Label(panel, "", 36, new Vector2(-570f, 55f), TextAnchor.MiddleLeft);
            for (int k = 0; k < Kinds.Length; k++)
            {
                int kind = k;
                float y = -20f - 72f * k;
                Label(panel, KindNames[k], 30, new Vector2(-570f, y), TextAnchor.MiddleLeft);
                for (int j = 0; j < MastermindUpgrades.MaxLevel; j++)
                {
                    Image pip = Node(panel.transform, "Pip").AddComponent<Image>();
                    pip.raycastTarget = false;
                    pip.color = EmptyPip;
                    Place(pip.gameObject, new Vector2(-395f + 50f * j, y), new Vector2(42f, 12f), new Vector2(0f, 0.5f));
                    pips[k, j] = pip;
                }
                bonusTexts[k] = Label(panel, "", 22, new Vector2(-225f, y), TextAnchor.MiddleLeft);
                bonusTexts[k].color = Muted;
                buyButtons[k] = Button(panel, new Vector2(30f, y), new Vector2(170f, 56f), 28, true, () => Buy(kind));
            }
            hintText = Label(panel, "", 21, new Vector2(-570f, -205f), TextAnchor.UpperLeft);
            hintText.color = Muted;
            hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hintText.rectTransform.sizeDelta = new Vector2(700f, 60f);
            hintText.rectTransform.pivot = new Vector2(0f, 1f);

            // Right: a commander of the picked type.
            Image card = Node(panel.transform, "Commander").AddComponent<Image>();
            card.color = new Color(1f, 1f, 1f, 0.07f);
            card.raycastTarget = false;
            Place(card.gameObject, new Vector2(400f, -75f), new Vector2(380f, 300f));
            Text cardTitle = Label(panel, "Commander", 20, new Vector2(400f, 45f));
            cardTitle.color = Muted;
            recruitTitle = Label(panel, "", 30, new Vector2(400f, 5f));
            recruitCrew = Label(panel, "", 22, new Vector2(400f, -35f));
            recruitButton = Button(panel, new Vector2(400f, -100f), new Vector2(300f, 60f), 30, true, Recruit, "Recruit");
            recruitCount = Label(panel, "", 20, new Vector2(400f, -165f));
            recruitCount.color = Muted;

            // Bottom: the commanders, the leader's latest reply, then Give up and Close.
            GameObject roster = Node(panel.transform, "Commanders");
            Place(roster, new Vector2(0f, -294f), new Vector2(1180f, 64f));
            var rosterRow = roster.AddComponent<HorizontalLayoutGroup>();
            rosterRow.spacing = 22f; rosterRow.childAlignment = TextAnchor.MiddleCenter;
            rosterRow.childControlWidth = rosterRow.childControlHeight = false;
            rosterRow.childForceExpandWidth = rosterRow.childForceExpandHeight = false;
            for (int i = 0; i < commanders.Length; i++)
            {
                int slot = i;
                commanders[i] = NativeRoundButton.Create(roster.transform, 62f, font, click =>
                {
                    if (click == PointerEventData.InputButton.Left) PickCommander(slot);
                });
                resting[i] = ModIcons.Badge(commanders[i].root.transform, ModIcons.Hourglass, 30f);
                fallen[i] = ModIcons.Badge(commanders[i].root.transform, ModIcons.Skull, 30f);
                commanders[i].SetActive(false);
            }
            resultText = Label(panel, "", 24, new Vector2(0f, -348f));
            giveUpButton = Button(panel, new Vector2(-390f, -390f), new Vector2(380f, 56f), 28, false, GiveUp);
            sellButton = Button(panel, new Vector2(0f, -390f), new Vector2(380f, 56f), 28, true, Sell, "Sell for");
            sellButton.input.gameObject.SetActive(false);
            closeButton = Button(panel, new Vector2(390f, -390f), new Vector2(200f, 56f), 30, false, Close);
            Label(closeButton, "Close", false, true, Color.white);

            root.SetActive(false);
        }

        /// <summary>White words that turn gold under the pointer, optionally after the game's coin.</summary>
        private static ChestButton Button(GameObject panel, Vector2 position, Vector2 size, int fontSize, bool coin, Action onClick, string prefix = null)
        {
            GameObject holder = Node(panel.transform, "Button");
            Place(holder, position, size);
            holder.AddComponent<Image>().color = Color.clear; // the hit area
            var layout = holder.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f; layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var button = new ChestButton();
            Text before = prefix != null ? NativeWidgets.Text(holder.transform, font, prefix, fontSize) : null;
            if (coin)
            {
                NativeWidgets.Coin(holder.transform, fontSize + 4f);
                button.coin = holder.transform.GetChild(holder.transform.childCount - 1).gameObject;
            }
            button.label = NativeWidgets.Text(holder.transform, font, "", fontSize);
            button.input = holder.AddComponent<TextButton>();
            button.input.label = button.label;
            button.input.extra = before;
            button.input.onClick = onClick;
            return button;
        }

        private static Text Label(GameObject panel, string value, int size, Vector2 position, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            Text text = NativeWidgets.Text(panel.transform, font, value, size, anchor);
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = anchor == TextAnchor.MiddleLeft ? new Vector2(0f, 0.5f) : new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(10f, size + 10f);
            return text;
        }

        private static GameObject Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(GameObject go)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Place(GameObject go, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static Font GameFont()
        {
            foreach (MainMenu menu in Resources.FindObjectsOfTypeAll<MainMenu>())
            {
                var prefab = Traverse.Create(menu).Field<ButtonWidget>("textButtonPrefab").Value;
                if (prefab && prefab.label && prefab.label.font) return prefab.label.font;
            }
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
    }

    /// <summary>Words used as a button: white (or their own colour), gold under the pointer, dim when unavailable.</summary>
    internal sealed class TextButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private static readonly Color Hover = new Color(1f, 0.83f, 0.42f, 1f);
        public Text label, extra;
        public Action onClick;
        public bool interactable = true;
        public Color normal = Color.white;
        private bool inside;

        public void OnPointerEnter(PointerEventData eventData)
        {
            inside = true;
            if (interactable) FabricWrapper.PostEvent(FabricID.uiHover);
            Paint();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            inside = false;
            Paint();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (!interactable) { FabricWrapper.PostEvent("UI/InGame/Error"); return; }
            FabricWrapper.PostEvent(FabricID.uiButtonClick);
            onClick?.Invoke();
        }

        public void Paint()
        {
            if (!label) return;
            Color color = !interactable ? new Color(normal.r, normal.g, normal.b, normal.a * 0.5f) : inside ? Hover : normal;
            if (label.color != color) label.color = color;
            if (extra && extra.color != color) extra.color = color;
        }

        private void OnDisable() => inside = false;
    }

    /// <summary>A click on the darkened screen around a panel.</summary>
    internal sealed class ClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        public Action onClick;

        public void OnPointerClick(PointerEventData eventData) => onClick?.Invoke();
    }
}
