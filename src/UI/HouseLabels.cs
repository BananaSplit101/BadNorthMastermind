using System.Collections.Generic;
using BadNorthMP.Net;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense;

namespace BadNorthMP.UI
{
    /// <summary>
    /// A label over each house with a squad in it (or on its way): whose commander it is, e.g. "Joren's Mildred",
    /// "Your Ulf", or just the commander in single player, tinted with that commander's banner colour. A squad still walking there shows faded, "on the way".
    /// From Net.HouseOccupants; shown to every player on the island.
    /// </summary>
    internal class HouseLabels : MonoBehaviour
    {
        private sealed class Label
        {
            public RectTransform rect;
            public Text text;
            public Image back;
            public CanvasGroup group;
        }

        private static readonly Color Chip = new Color(0.94f, 0.91f, 0.85f, 0.95f);

        private GameObject canvasObject;
        private Canvas canvas;
        private readonly List<Label> labels = new List<Label>();

        private void Update()
        {
            HouseOccupants.Update();
            Island island = GameAccess.CurrentIsland;
            var level = Singleton<LevelCamera>.instance;
            Camera camera = level ? level.cameraRef : null;
            bool show = island && island.state == Island.State.Playing && island.village && island.village.houses != null &&
                camera && HouseOccupants.Current.Count > 0 && Profile.campaign != null && !LoadGate.Blocking;
            if (!show)
            {
                if (canvasObject && canvasObject.activeSelf) canvasObject.SetActive(false);
                return;
            }
            if (!canvasObject) Build();
            if (!canvasObject.activeSelf) canvasObject.SetActive(true);

            House[] houses = island.village.houses;
            int used = 0;
            foreach (var pair in HouseOccupants.Current)
            {
                if (pair.Key < 0 || pair.Key >= houses.Length || !houses[pair.Key]) continue;
                if (pair.Value.hero < 0 || pair.Value.hero >= Profile.campaign.heroes.Count) continue;
                var location = houses[pair.Key].GetComponent<SquadReplenishLocation>();
                Vector3 top = location && location.flagTransform ? location.flagTransform.position : houses[pair.Key].transform.position;
                Vector3 screen = camera.WorldToScreenPoint(top + Vector3.up * 0.35f);
                if (screen.z < 0f) continue;
                if (used == labels.Count) labels.Add(NewLabel());
                Label label = labels[used++];
                label.rect.gameObject.SetActive(true);
                float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
                label.rect.anchoredPosition = ((Vector2)screen - new Vector2(Screen.width, Screen.height) * 0.5f) / scale;
                label.text.text = Text(pair.Value);
                label.back.color = Color.Lerp(Chip, Profile.campaign.heroes[pair.Value.hero].color, 0.3f);
                label.text.fontStyle = pair.Value.inside ? FontStyle.Normal : FontStyle.Italic;
                label.group.alpha = pair.Value.inside ? 1f : 0.7f;
            }
            for (int i = used; i < labels.Count; i++)
                if (labels[i].rect.gameObject.activeSelf) labels[i].rect.gameObject.SetActive(false);
        }

        private static string Text(HouseOccupants.Entry entry)
        {
            string commander = Profile.campaign.heroes[entry.hero].dbgName;
            string owner = HouseOccupants.OwnerName(entry.hero);
            string text = owner == "" ? commander : owner == "You" ? "Your " + commander : owner + "'s " + commander;
            return entry.inside ? text : text + " · on the way";
        }

        private void Build()
        {
            canvasObject = GameStyle.Canvas("BNMP House Labels", 530);
            canvas = canvasObject.GetComponent<Canvas>();
            canvasObject.GetComponent<GraphicRaycaster>().enabled = false; // never takes a click
        }

        private Label NewLabel()
        {
            RectTransform panel = GameStyle.Panel(canvasObject.transform, new RectOffset(10, 10, 3, 4), 0f);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0f);
            Image back = panel.GetComponent<Image>();
            back.color = Chip;
            Text text = GameStyle.Label(panel, "", 17);
            var group = panel.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = group.interactable = false;
            foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            return new Label { rect = panel, text = text, back = back, group = group };
        }
    }
}
