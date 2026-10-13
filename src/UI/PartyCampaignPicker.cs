using BadNorthMP.Game;
using BadNorthMP.Net;
using UnityEngine;
using UnityEngine.UI;

namespace BadNorthMP.UI
{
    // The host picks one of the campaign leader's multiplayer save slots before the draft. It never touches the
    // host's single-player campaigns.
    internal sealed class PartyCampaignPicker : MonoBehaviour
    {
        private static PartyCampaignPicker instance;
        private GameObject canvasObject, panel;
        private int newSlot = -1, confirmDelete = -1;
        private float confirmUntil;
        private string catalogKey;
        public static void Open()
        {
            if (!Allowed()) return;
            if (!instance)
            {
                var go = new GameObject("BNMP Multiplayer Save Selection");
                DontDestroyOnLoad(go); instance = go.AddComponent<PartyCampaignPicker>();
            }
            MultiplayerScreen.CloseForBattle();
            instance.newSlot = -1; instance.Show();
        }
        private static bool Allowed() => SteamSession.Instance && SteamSession.Instance.InLobby &&
            SteamSession.Instance.IsHost && Updater.Compatible && Roles.HasMastermind && !PartyFlow.Active;
        private void Update()
        {
            if (!canvasObject || !canvasObject.activeSelf) return;
            if (!Allowed()) { canvasObject.SetActive(false); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { Back(); return; }
            if (newSlot < 0 && catalogKey != CatalogKey()) Show();
            // The delete confirmation runs out.
            if (confirmDelete >= 0 && Time.unscaledTime > confirmUntil) { confirmDelete = -1; Show(); }
        }
        private string CatalogKey() => PartyFlow.CatalogReady + ":" + string.Join("|", PartyFlow.SaveDetails);
        private void Build()
        {
            canvasObject = GameStyle.Canvas("BNMP Multiplayer Saves", 660);
            var shade = Node(canvasObject.transform, "Backdrop");
            GameStyle.Stretch((RectTransform)shade.transform);
            shade.AddComponent<Image>().color = new Color(.12f, .12f, .12f, .9f);
            panel = Node(canvasObject.transform, "Saved Games");
            var rect = (RectTransform)panel.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f);
            rect.sizeDelta = new Vector2(650, 830); panel.AddComponent<Image>().color = new Color(.76f, .72f, .66f);
            var layout = panel.AddComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(24,24,20,20);
            layout.spacing = 10; layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
        }
        private void Show()
        {
            if (!canvasObject) Build();
            canvasObject.SetActive(true);
            for (int i = panel.transform.childCount - 1; i >= 0; i--) DestroyImmediate(panel.transform.GetChild(i).gameObject);
            catalogKey = CatalogKey();
            if (newSlot >= 0) { ShowNew(); return; }
            Label(panel.transform, "Multiplayer Saved Games", 38, 55);
            Label(panel.transform, "Saved on the campaign leader's game", 23, 35);
            if (!PartyFlow.CatalogReady) Label(panel.transform, "Waiting for the campaign leader's saves...", 26, 110);
            else for (int i = 0; i < MultiplayerCampaigns.SlotCount; i++)
            {
                int slot = i;
                if (PartyFlow.Occupied[i])
                {
                    // The campaign, and beside it Delete (click twice).
                    var row = Node(panel.transform, "Campaign row");
                    var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
                    rowLayout.spacing = 10; rowLayout.childForceExpandWidth = false; rowLayout.childControlWidth = rowLayout.childControlHeight = true;
                    row.AddComponent<LayoutElement>().preferredHeight = 110;
                    var button = Button("Campaign " + (i + 1) + "\n" + PartyFlow.SaveDetails[i], () => Continue(slot), 110, row.transform);
                    button.interactable = PartyFlow.Saved[i];
                    button.GetComponentInChildren<Text>().fontSize = 27;
                    button.GetComponent<LayoutElement>().flexibleWidth = 1;
                    bool confirming = confirmDelete == slot && Time.unscaledTime < confirmUntil;
                    var delete = Button(confirming ? "Really\ndelete?" : "Delete", () => Delete(slot), 110, row.transform);
                    delete.GetComponent<LayoutElement>().preferredWidth = 140;
                    delete.GetComponentInChildren<Text>().fontSize = 25;
                    if (confirming) delete.GetComponent<Image>().color = new Color(.93f, .62f, .55f);
                }
                else Button("New Game", () => { newSlot = slot; Show(); }, 110);
            }
            Button("Back", Back, 65);
        }
        private void ShowNew()
        {
            Label(panel.transform, "New Multiplayer Game", 38, 70);
            Label(panel.transform, "Save slot " + (newSlot + 1), 25, 45);
            Button("Difficulty: " + (Voxels.TowerDefense.Difficulty)PartyFlow.Difficulty,
                () => { PartyFlow.Difficulty = PartyFlow.Difficulty % 4 + 1; Show(); }, 90);
            Button((PartyFlow.AllowReplays ? "[X] " : "[ ] ") + "Allow Level Restarts",
                () => { PartyFlow.AllowReplays = !PartyFlow.AllowReplays; Show(); }, 90);
            Label(panel.transform, "Tutorial is skipped in multiplayer.\nDefenders choose their commanders next.", 26, 100);
            Button("Begin", Begin, 90);
            Button("Back", () => { newSlot = -1; Show(); }, 65);
        }
        /// <summary>First click asks, a second click within 3 seconds deletes it on the campaign leader's game.</summary>
        private void Delete(int slot)
        {
            if (!Allowed() || !PartyFlow.CatalogReady || !PartyFlow.Occupied[slot]) return;
            if (confirmDelete != slot || Time.unscaledTime > confirmUntil)
            {
                confirmDelete = slot;
                confirmUntil = Time.unscaledTime + 3f;
                FabricWrapper.PostEvent("UI/Menu/DeleteSaveSelect");
                Show();
                return;
            }
            confirmDelete = -1;
            FabricWrapper.PostEvent("UI/Menu/DeleteSave");
            PartyFlow.RequestDelete(slot);
            Show();
        }

        private void Continue(int slot)
        {
            if (!Allowed() || !PartyFlow.CatalogReady || !PartyFlow.Saved[slot]) return;
            PartyFlow.SaveChoice = slot; PartyFlow.NewSaveSlot = -1;
            canvasObject.SetActive(false); PartyFlow.StartDraft();
        }
        private void Begin()
        {
            if (!Allowed() || !PartyFlow.CatalogReady || newSlot < 0 || PartyFlow.Occupied[newSlot])
            { newSlot = -1; Show(); return; }
            PartyFlow.SaveChoice = -1; PartyFlow.NewSaveSlot = MultiplayerCampaigns.FirstSlot + newSlot;
            canvasObject.SetActive(false); PartyFlow.StartDraft();
        }
        private void Back() { if (canvasObject) canvasObject.SetActive(false); MultiplayerScreen.Open(); }
        private GameObject Node(Transform parent, string name)
        { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go; }
        private Text Label(Transform parent, string value, int size, float height)
        {
            var go = Node(parent, "Label"); var text = go.AddComponent<Text>(); text.font = GameStyle.Font; text.text = value;
            text.fontSize = size; text.color = GameStyle.Ink; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            go.AddComponent<LayoutElement>().preferredHeight = height; return text;
        }
        private Button Button(string value, UnityEngine.Events.UnityAction action, float height, Transform parent = null)
        {
            var go = Node(parent ? parent : panel.transform, "Campaign option"); var image = go.AddComponent<Image>();
            image.sprite = GameStyle.ButtonSprite; image.type = GameStyle.ButtonSprite ? Image.Type.Sliced : Image.Type.Simple;
            image.color = new Color(.95f,.90f,.83f); go.AddComponent<LayoutElement>().preferredHeight = height;
            var button = go.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
            var label = Label(go.transform, value, 32, 0); var rect = (RectTransform)label.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12, 4); rect.offsetMax = new Vector2(-12, -4);
            return button;
        }
        private void OnDestroy() { if (canvasObject) Destroy(canvasObject); }
    }
}
