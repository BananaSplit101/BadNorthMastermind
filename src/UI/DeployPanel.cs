using System;
using System.Collections.Generic;
using System.Text;
using BadNorthMP.Net;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense;

namespace BadNorthMP.UI
{
    /// <summary>
    /// Before an island starts (DeployFlow): each defender clicks which of their commanders go (their portraits, the
    /// picked ones highlighted) and presses Ready; everyone sees who's still choosing. The mastermind, and anyone
    /// without squads to choose, only see that.
    /// </summary>
    internal class DeployPanel : MonoBehaviour
    {
        private const float PortraitSize = 96f;
        private static readonly Color PickedColor = new Color(1f, 0.83f, 0.42f, 1f);

        private GameObject canvasObject;
        private Text title, status, readyLabel;
        private Transform cards;
        private Button ready;
        private readonly Dictionary<int, Image> cardImages = new Dictionary<int, Image>();
        private string signature = "";

        private void Update()
        {
            if (!DeployFlow.Choosing)
            {
                if (canvasObject && canvasObject.activeSelf) canvasObject.SetActive(false);
                return;
            }
            if (!canvasObject) Build();
            if (!canvasObject.activeSelf) { canvasObject.SetActive(true); signature = ""; }
            Refresh();
        }

        private void Refresh()
        {
            PartyDeployment plan = DeployFlow.Current;
            DeploySeat mine = DeployFlow.Mine;
            List<HeroDefinition> heroes = Profile.campaign != null ? Profile.campaign.heroes : null;
            bool choosing = mine != null && mine.Choosing && heroes != null;
            bool rescuer = mine != null && plan.rescueHero >= 0 && plan.rescueOwner == mine.player;

            string text = choosing ? (mine.quota == 1 ? "Choose the commander you'll bring" : $"Choose {mine.quota} commanders to bring")
                : rescuer ? $"You command the rescued {Name(plan.rescueHero)} on this island"
                : mine != null ? "Your commanders sit this island out"
                : "The defenders are choosing their commanders";
            if (choosing && rescuer) text += $"\n<size=18>You also command the rescued {Name(plan.rescueHero)}</size>";
            title.text = text;

            string cardsKey = choosing ? plan.token + ":" + string.Join(",", mine.candidates.ConvertAll(id => id.ToString()).ToArray()) : "";
            if (cardsKey != signature)
            {
                signature = cardsKey;
                for (int i = cards.childCount - 1; i >= 0; i--) Destroy(cards.GetChild(i).gameObject);
                cardImages.Clear();
                if (choosing) foreach (int id in mine.candidates) if (id < heroes.Count) Card(id, heroes[id]);
                cards.gameObject.SetActive(choosing);
            }
            if (choosing)
                foreach (var card in cardImages) card.Value.color = mine.picks.Contains(card.Key) ? PickedColor : GameStyle.ButtonColor;

            ready.gameObject.SetActive(choosing);
            if (choosing)
            {
                ready.interactable = mine.picks.Count >= 1 && mine.picks.Count <= mine.quota;
                readyLabel.text = mine.ready ? "Not ready" : "Ready";
            }

            var line = new StringBuilder();
            foreach (DeploySeat seat in plan.seats)
            {
                if (!seat.Choosing) continue;
                if (line.Length > 0) line.Append("     ");
                line.Append(SteamSession.NameOf(new CSteamID(seat.player))).Append(seat.ready ? ": ready" : ": choosing...");
            }
            if (plan.secondsLeft < 60f)
                line.Append($"\n<size=16>Landing in {Mathf.CeilToInt(plan.secondsLeft)} s, ready or not</size>");
            status.text = line.ToString();
        }

        private static string Name(int hero)
        {
            var heroes = Profile.campaign != null ? Profile.campaign.heroes : null;
            return heroes != null && hero >= 0 && hero < heroes.Count ? heroes[hero].dbgName : "commander";
        }

        private void Card(int id, HeroDefinition hero)
        {
            var go = new GameObject("Commander", typeof(RectTransform));
            go.transform.SetParent(cards, false);
            var image = go.AddComponent<Image>();
            image.sprite = GameStyle.ButtonSprite;
            image.type = GameStyle.ButtonSprite ? Image.Type.Sliced : Image.Type.Simple;
            image.color = GameStyle.ButtonColor;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => DeployFlow.Toggle(id));
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 8);
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = layout.childForceExpandWidth = false;
            go.AddComponent<LayoutElement>().preferredWidth = 150f;

            var holder = new GameObject("Portrait", typeof(RectTransform));
            holder.transform.SetParent(go.transform, false);
            var size = holder.AddComponent<LayoutElement>();
            size.minWidth = size.preferredWidth = size.minHeight = size.preferredHeight = PortraitSize;
            try { GameUi.Portrait(holder.transform, hero, PortraitSize); }
            catch (Exception e) { ModLog.Warn("Commander portrait unavailable: " + e.Message); }
            GameStyle.Label(go.transform, hero.dbgName, 18);
            var classDef = hero.classUpgrade != null ? hero.classUpgrade.definition : null;
            GameStyle.Label(go.transform, classDef ? classDef.dbgName : "Militia", 15);
            cardImages[id] = image;
        }

        private void Build()
        {
            canvasObject = GameStyle.Canvas("BNMP Deploy Canvas", 550); // over the mastermind screen (500), under notices
            RectTransform panel = GameStyle.Panel(canvasObject.transform, new RectOffset(22, 22, 14, 16), 10f);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = new Vector2(0f, -110f);
            title = GameStyle.Label(panel, "", 26);
            cards = GameStyle.Row(panel, 160f, 12f);
            status = GameStyle.Label(panel, "", 18);
            ready = GameStyle.Button(panel, "Ready", () => DeployFlow.SetReady(!(DeployFlow.Mine != null && DeployFlow.Mine.ready)), 220f);
            readyLabel = ready.GetComponentInChildren<Text>();
        }
    }
}
