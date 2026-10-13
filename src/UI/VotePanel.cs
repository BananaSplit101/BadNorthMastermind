using System.Collections.Generic;
using System.Text;
using BadNorthMP.Net;
using BadNorthMP.Spectator;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI;
using Voxels.TowerDefense.UI.UpgradeScreen;

namespace BadNorthMP.UI
{
    /// <summary>
    /// The party's island vote on the campaign map: a header with every voter's Steam picture (dimmed until they vote)
    /// and Next Turn, and each voted island's voters above its name. The mastermind sees it read-only. Rebuilt from
    /// IslandVote's state, so it survives map reloads.
    /// </summary>
    internal class VotePanel : MonoBehaviour
    {
        // A touch more see-through than GameStyle's panels.
        private static readonly Color PanelColor = new Color(0.76f, 0.72f, 0.66f, 0.95f);
        private const float AvatarSize = 40f, MarkerAvatarSize = 34f;

        private sealed class Marker
        {
            public GameObject root;
            public RectTransform rect;
            public LevelNodeUIProxy proxy;
        }

        private GameObject canvasObject;
        private RectTransform canvasRect;
        private Text headerText, launchText;
        private Button cancelButton;
        private int lastLaunchSecond = -1;
        private Transform votersRow, nextTurnVoters;
        private Button nextTurnButton;
        private readonly Dictionary<int, Marker> markers = new Dictionary<int, Marker>();
        private string signature = "";
        private CampaignMapUI map;
        private CampaignMapLevelNodes nodes;
        private float nextMapSearch;

        private void Update()
        {
            bool show = PartyFlow.Deployed && OnMap();
            if (!show)
            {
                if (canvasObject && canvasObject.activeSelf) canvasObject.SetActive(false);
                return;
            }
            if (!canvasObject) Build();
            if (!canvasObject.activeSelf) { canvasObject.SetActive(true); signature = ""; }
            Refresh();
            PlaceMarkers();
        }

        /// <summary>Viewing the campaign map, with no upgrade menu open over it.</summary>
        private bool OnMap()
        {
            var stack = Singleton<Stack>.instance;
            if (!stack || stack.stateLoading.gameObject.activeInHierarchy || GameAccess.CurrentIsland) return false;
            bool viewing = Roles.IsDefender && !SpectatorSession.Active ? stack.stateCampaign.gameObject.activeInHierarchy : SpectatorSession.ViewingCampaign;
            if (!viewing) return false;
            var upgrades = SuperUpgradeMenu.instance;
            if (upgrades && upgrades.isOpen) return false;
            if ((!map || !map.isActiveAndEnabled) && Time.unscaledTime >= nextMapSearch)
            {
                nextMapSearch = Time.unscaledTime + 1f;
                map = FindObjectOfType<CampaignMapUI>();
                nodes = map ? Traverse.Create(map).Field<CampaignMapLevelNodes>("nodes").Value : null;
            }
            return map && map.isActiveAndEnabled;
        }

        // ---- Content ------------------------------------------------------------------------------

        private void Refresh()
        {
            List<ulong> voters = IslandVote.Voters();
            var text = new StringBuilder();
            foreach (ulong voter in voters)
                text.Append(voter).Append(IslandVote.Votes.TryGetValue(voter, out int option) ? option : -9)
                    .Append(SteamAvatars.Loaded(voter) ? '+' : '-');
            text.Append(IslandVote.NextTurnAllowed).Append(IslandVote.CanVote).Append(Mathf.CeilToInt(IslandVote.SecondsLeft))
                .Append('/').Append(Mathf.CeilToInt(IslandVote.LaunchSecondsLeft));
            string current = text.ToString();
            if (current == signature) return;
            signature = current;

            headerText.text = Header(voters);
            // The launch countdown: big, in the middle of the screen, ticking every second.
            float launch = IslandVote.LaunchSecondsLeft;
            int second = launch >= 0f ? Mathf.CeilToInt(launch) : -1;
            launchText.gameObject.SetActive(second >= 0);
            if (second >= 0)
            {
                var winners = new List<string>();
                foreach (int option in VoteTally.Leaders(IslandVote.Votes)) winners.Add(IslandVote.Describe(option));
                launchText.text = $"Launching game in {Mathf.Max(1, second)}\n<size=26>{string.Join(" or ", winners.ToArray())}</size>";
                if (second != lastLaunchSecond) FabricWrapper.PostEvent("UI/InGame/TapRight");
            }
            lastLaunchSecond = second;
            cancelButton.gameObject.SetActive(IslandVote.CanVote && IslandVote.HasVoted);
            Clear(votersRow);
            foreach (ulong voter in voters)
                Avatar(votersRow, voter, AvatarSize, IslandVote.Votes.ContainsKey(voter) ? 1f : 0.35f);
            nextTurnButton.gameObject.SetActive(IslandVote.CanVote);
            nextTurnButton.interactable = IslandVote.NextTurnAllowed;
            Clear(nextTurnVoters);
            foreach (var vote in IslandVote.Votes)
                if (vote.Value == IslandVote.NextTurn) Avatar(nextTurnVoters, vote.Key, MarkerAvatarSize, 1f);

            // One marker per voted island, showing who voted for it.
            var options = new HashSet<int>();
            foreach (int option in IslandVote.Votes.Values) if (option >= 0) options.Add(option);
            var gone = new List<int>();
            foreach (int option in markers.Keys) if (!options.Contains(option)) gone.Add(option);
            foreach (int option in gone) { Destroy(markers[option].root); markers.Remove(option); }
            foreach (int option in options)
            {
                if (!markers.TryGetValue(option, out Marker marker)) markers[option] = marker = NewMarker();
                Clear(marker.root.transform);
                foreach (var vote in IslandVote.Votes)
                    if (vote.Value == option) Avatar(marker.root.transform, vote.Key, MarkerAvatarSize, 1f);
                LevelNode node = IslandVote.Level(option);
                marker.proxy = node && nodes ? nodes.GetProxy(node) : null;
            }
        }

        private static string Header(List<ulong> voters)
        {
            int voted = IslandVote.Votes.Count;
            if (voted == 0)
                return IslandVote.CanVote ? "Click an island to vote where the party goes next" : "The defenders are voting where to go next";
            var leaders = new List<string>();
            foreach (int option in VoteTally.Leaders(IslandVote.Votes)) leaders.Add(IslandVote.Describe(option));
            string lead = leaders.Count == 1 ? leaders[0] + " leads" : "Tied: " + string.Join(" / ", leaders.ToArray());
            float left = IslandVote.SecondsLeft;
            if (IslandVote.LaunchSecondsLeft >= 0f) return lead + "  (" + voted + "/" + voters.Count + " voted, launching)";
            return lead + "  (" + voted + "/" + voters.Count + " voted" + (left >= 0f ? ", deciding in " + Mathf.CeilToInt(left) + "s" : "") + ")";
        }

        private void PlaceMarkers()
        {
            foreach (Marker marker in markers.Values)
            {
                bool visible = marker.proxy && marker.proxy.gameObject.activeInHierarchy;
                if (marker.root.activeSelf != visible) marker.root.SetActive(visible);
                if (!visible) continue;
                // The island's name tag is placed at the island's screen point every frame (CampaignMapLevelNodes).
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, marker.proxy.transform.position, null, out Vector2 local);
                marker.rect.anchoredPosition = local + new Vector2(0f, 58f);
            }
        }

        // ---- Widgets ------------------------------------------------------------------------------

        private void Build()
        {
            canvasObject = GameStyle.Canvas("BNMP Vote Canvas", 450);
            canvasRect = (RectTransform)canvasObject.transform;

            RectTransform panel = GameStyle.Panel(canvasObject.transform, new RectOffset(18, 18, 10, 12), 6f);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0f, -14f);
            panel.GetComponent<Image>().color = PanelColor;

            headerText = GameStyle.Label(panel, "", 24);
            votersRow = GameStyle.Row(panel, AvatarSize, 6f);
            Transform nextRow = GameStyle.Row(panel, 44f, 6f);
            nextTurnButton = GameStyle.Button(nextRow, "Vote: Next Turn", () => IslandVote.Cast(IslandVote.NextTurn), 230f);
            nextTurnVoters = GameStyle.Row(nextRow, MarkerAvatarSize, 6f);
            cancelButton = GameStyle.Button(nextRow, "Cancel my vote", () => IslandVote.Cast(IslandVote.None), 230f);
            cancelButton.gameObject.SetActive(false);

            launchText = NativeWidgets.Text(canvasObject.transform, GameStyle.Font, "", 48);
            var launchRect = launchText.rectTransform;
            launchRect.anchorMin = launchRect.anchorMax = new Vector2(0.5f, 0.5f);
            launchRect.anchoredPosition = new Vector2(0f, 160f);
            launchRect.sizeDelta = new Vector2(10f, 60f);
            launchText.gameObject.SetActive(false);
        }

        private Marker NewMarker()
        {
            var go = new GameObject("Vote Marker", typeof(RectTransform));
            go.transform.SetParent(canvasObject.transform, false);
            go.transform.SetAsFirstSibling();
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0f);
            var background = go.AddComponent<Image>();
            background.sprite = GameStyle.PanelSprite;
            background.type = GameStyle.PanelSprite ? Image.Type.Sliced : Image.Type.Simple;
            background.color = PanelColor;
            background.raycastTarget = false;
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 4f;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = layout.childForceExpandWidth = false;
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            return new Marker { root = go, rect = rect };
        }

        private void Avatar(Transform parent, ulong player, float size, float alpha)
        {
            var go = new GameObject("Avatar", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            Sprite sprite = SteamAvatars.Get(player);
            image.sprite = sprite;
            Color color = sprite ? Color.white : SteamAvatars.Fallback(player);
            color.a = alpha;
            image.color = color;
            var element = go.AddComponent<LayoutElement>();
            element.minWidth = element.preferredWidth = size;
            element.minHeight = element.preferredHeight = size;
            if (sprite) return;
            // Until Steam sends the picture: the player's initial.
            string name = SteamSession.NameOf(new CSteamID(player));
            Text initial = GameStyle.Label(go.transform, name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?", (int)(size * 0.55f));
            Destroy(initial.GetComponent<LayoutElement>());
            var rect = initial.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--) Destroy(parent.GetChild(i).gameObject);
        }
    }
}
