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
    /// Before an island starts (DeployFlow): each defender picks which of their commanders go and presses Ready, in the
    /// game's own loadout screen (or this panel's cards if that isn't there). Everyone else sees each defender's picks
    /// so far and who's ready.
    /// </summary>
    internal class DeployPanel : MonoBehaviour
    {
        private const float PortraitSize = 96f, PickSize = 64f;
        private static readonly Color PickedColor = new Color(1f, 0.83f, 0.42f, 1f);

        private GameObject canvasObject;
        private Text title, status, readyLabel;
        private Transform cards, overview;
        private Button ready;
        private readonly Dictionary<int, Image> cardImages = new Dictionary<int, Image>();
        private string signature = "", overviewSignature = "";

        private void Update()
        {
            // While the game's own loadout screen is up for this player's choice, it is the picker.
            if (!DeployFlow.Choosing || NativeLoadout.Showing)
            {
                if (canvasObject && canvasObject.activeSelf) canvasObject.SetActive(false);
                return;
            }
            if (!canvasObject) Build();
            if (!canvasObject.activeSelf) { canvasObject.SetActive(true); signature = overviewSignature = null; }
            Refresh();
        }

        private void Refresh()
        {
            PartyDeployment plan = DeployFlow.Current;
            DeploySeat mine = DeployFlow.Mine;
            List<HeroDefinition> heroes = Profile.campaign != null ? Profile.campaign.heroes : null;
            bool choosing = mine != null && mine.Choosing && heroes != null;
            bool rescuer = mine != null && plan.rescueHero >= 0 && plan.rescueOwner == mine.player;
            bool native = !NativeLoadout.Unavailable;
            bool cardsShown = choosing && !native;

            string text = choosing && native ? (mine.ready ? "You're ready: waiting for the others" : "Choose your commanders")
                : choosing ? (mine.quota == 1 ? "Choose the commander you'll bring" : $"Choose {mine.quota} commanders to bring")
                : rescuer ? $"You command the rescued {Name(plan.rescueHero)} on this island"
                : mine != null ? "Your commanders sit this island out"
                : "The defenders are choosing their commanders";
            if (choosing && rescuer) text += $"\n<size=18>You also command the rescued {Name(plan.rescueHero)}</size>";
            title.text = text;

            string cardsKey = cardsShown ? plan.token + ":" + string.Join(",", mine.candidates.ConvertAll(id => id.ToString()).ToArray()) : "";
            if (cardsKey != signature)
            {
                signature = cardsKey;
                for (int i = cards.childCount - 1; i >= 0; i--) Destroy(cards.GetChild(i).gameObject);
                cardImages.Clear();
                if (cardsShown) foreach (int id in mine.candidates) if (id < heroes.Count) Card(id, heroes[id]);
            }
            // An empty row would still hold its space (it did: a blank gap on the mastermind's panel).
            if (cards.gameObject.activeSelf != cardsShown) cards.gameObject.SetActive(cardsShown);
            Overview(plan, heroes, !cardsShown);
            if (cardsShown)
                foreach (var card in cardImages) card.Value.color = mine.picks.Contains(card.Key) ? PickedColor : GameStyle.ButtonColor;

            ready.gameObject.SetActive(choosing);
            if (choosing)
            {
                ready.interactable = native || (mine.picks.Count >= 1 && mine.picks.Count <= mine.quota);
                // Un-readying reopens the game's loadout screen with the current picks.
                readyLabel.text = native ? "Change commanders" : mine.ready ? "Not ready" : "Ready";
            }

            // Who's still choosing: in the overview when it's up, else on this line.
            var line = new StringBuilder();
            if (cardsShown)
                foreach (DeploySeat seat in plan.seats)
                {
                    if (!seat.Choosing) continue;
                    if (line.Length > 0) line.Append("     ");
                    line.Append(SteamSession.NameOf(new CSteamID(seat.player))).Append(seat.ready ? ": ready" : ": choosing...");
                }
            if (plan.secondsLeft < 60f)
                line.Append(line.Length > 0 ? "\n" : "").Append($"<size=16>Landing in {Mathf.CeilToInt(plan.secondsLeft)} s, ready or not</size>");
            status.text = line.ToString();
            if (status.gameObject.activeSelf != (line.Length > 0)) status.gameObject.SetActive(line.Length > 0);
        }

        /// <summary>Each choosing defender's name, ready or not, and the portraits of the commanders they've picked so far.</summary>
        private void Overview(PartyDeployment plan, List<HeroDefinition> heroes, bool shown)
        {
            var key = new StringBuilder();
            if (shown && heroes != null)
            {
                key.Append(plan.token);
                foreach (DeploySeat seat in plan.seats)
                    if (seat.Choosing)
                        key.Append('|').Append(seat.player).Append(seat.ready ? '+' : '-').Append(string.Join(",", seat.picks.ConvertAll(id => id.ToString()).ToArray()));
                key.Append('|').Append(plan.rescueHero).Append('@').Append(plan.rescueOwner);
            }
            string built = key.ToString();
            if (built == overviewSignature) return;
            overviewSignature = built;
            for (int i = overview.childCount - 1; i >= 0; i--) Destroy(overview.GetChild(i).gameObject);
            bool any = false;
            if (built.Length > 0)
                foreach (DeploySeat seat in plan.seats)
                {
                    if (!seat.Choosing) continue;
                    any = true;
                    var picked = new List<int>(seat.picks);
                    if (plan.rescueHero >= 0 && plan.rescueOwner == seat.player && !picked.Contains(plan.rescueHero)) picked.Add(plan.rescueHero);
                    Group(seat, picked, heroes);
                }
            if (overview.gameObject.activeSelf != any) overview.gameObject.SetActive(any);
        }

        private void Group(DeploySeat seat, List<int> picked, List<HeroDefinition> heroes)
        {
            var group = new GameObject("Defender", typeof(RectTransform));
            group.transform.SetParent(overview, false);
            var layout = group.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = layout.childForceExpandWidth = false;
            string name = SteamSession.NameOf(new CSteamID(seat.player));
            GameStyle.Label(group.transform, name + (seat.ready ? " · ready" : " · choosing..."), 18);
            Transform row = GameStyle.Row(group.transform, PickSize, 6f);
            int shown = 0;
            foreach (int id in picked)
            {
                if (id < 0 || id >= heroes.Count) continue;
                Portrait(row, heroes[id], PickSize);
                shown++;
            }
            if (shown == 0) GameStyle.Label(row, "no commanders picked yet", 15);
        }

        private static void Portrait(Transform parent, HeroDefinition hero, float size)
        {
            var holder = new GameObject("Portrait", typeof(RectTransform));
            holder.transform.SetParent(parent, false);
            var element = holder.AddComponent<LayoutElement>();
            element.minWidth = element.preferredWidth = element.minHeight = element.preferredHeight = size;
            try { GameUi.Portrait(holder.transform, hero, size); }
            catch (Exception e) { ModLog.Warn("Commander portrait unavailable: " + e.Message); }
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

            Portrait(go.transform, hero, PortraitSize);
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
            overview = GameStyle.Row(panel, PickSize + 28f, 28f);
            status = GameStyle.Label(panel, "", 18);
            ready = GameStyle.Button(panel, "Ready", () => DeployFlow.SetReady(!(DeployFlow.Mine != null && DeployFlow.Mine.ready)), 220f);
            readyLabel = ready.GetComponentInChildren<Text>();
        }
    }
}
