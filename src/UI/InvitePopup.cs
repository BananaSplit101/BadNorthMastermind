using BadNorthMP.Net;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    /// <summary>
    /// A Steam invite, offered in game: a card at the right edge with the inviter's picture and Join / Decline, so
    /// nobody has to open the Steam overlay to accept. It works anywhere (menus, map, battle) and sits above every
    /// other screen. A newer invite replaces the card; it goes away by itself after a minute, or once we're in that
    /// lobby (also when the invite was accepted through Steam).
    /// </summary>
    internal class InvitePopup : MonoBehaviour
    {
        private const float Lifetime = 60f, AvatarSize = 64f;
        private static readonly Color PanelColor = new Color(0.76f, 0.72f, 0.66f, 0.97f);
        private static readonly Color ButtonColor = new Color(0.98f, 0.92f, 0.85f, 1f);
        private static readonly Color DarkText = new Color(0.2f, 0.17f, 0.15f, 1f);

        private GameObject canvasObject;
        private Image avatar;
        private Text initial, message, note;
        private Font font;
        private Sprite buttonSprite, panelSprite;
        private CSteamID inviter = CSteamID.Nil, lobby = CSteamID.Nil;
        private float shownAt;

        private static SteamSession Session => SteamSession.Instance;

        private void Start() => Session.InviteReceived += Show;

        private void OnDestroy()
        {
            if (Session) Session.InviteReceived -= Show;
        }

        private void Show(CSteamID from, CSteamID invitedTo)
        {
            inviter = from;
            lobby = invitedTo;
            shownAt = Time.unscaledTime;
            if (!canvasObject) Build();
            string name = SteamSession.NameOf(from);
            message.text = $"<b>{name}</b> invited you to play\nBad North Multiplayer";
            note.text = Session.InLobby ? "Joining leaves your current lobby" : "";
            note.gameObject.SetActive(Session.InLobby);
            avatar.sprite = null;
            initial.text = name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?";
            canvasObject.SetActive(true);
        }

        private void Update()
        {
            if (!canvasObject || !canvasObject.activeSelf) return;
            if (Time.unscaledTime - shownAt > Lifetime || (Session.InLobby && Session.Lobby == lobby)) { Hide(); return; }
            // Steam may only have the picture a moment later.
            if (!avatar.sprite)
            {
                Sprite sprite = SteamAvatars.Get(inviter.m_SteamID);
                avatar.sprite = sprite;
                avatar.color = sprite ? Color.white : SteamAvatars.Fallback(inviter.m_SteamID);
                initial.gameObject.SetActive(!sprite);
            }
        }

        private void Join()
        {
            ModLog.Info($"Accepted {SteamSession.NameOf(inviter)}'s invite");
            Session.Join(lobby);
            Hide();
        }

        private void Hide()
        {
            if (canvasObject) canvasObject.SetActive(false);
            inviter = lobby = CSteamID.Nil;
        }

        // ---- Widgets ------------------------------------------------------------------------------

        private void Build()
        {
            FindGameStyle();
            canvasObject = new GameObject("BNMP Invite Canvas");
            DontDestroyOnLoad(canvasObject);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31500; // above everything, the loading gate (31000) included
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            canvasObject.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Invite", typeof(RectTransform));
            panel.transform.SetParent(canvasObject.transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.72f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-24f, 0f);
            var background = panel.AddComponent<Image>();
            background.sprite = panelSprite;
            background.type = panelSprite ? Image.Type.Sliced : Image.Type.Simple;
            background.color = PanelColor;
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 14, 14);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = layout.childForceExpandWidth = false;
            var fitter = panel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            Transform top = Row(panel.transform, AvatarSize, 14f);
            var avatarObject = new GameObject("Avatar", typeof(RectTransform));
            avatarObject.transform.SetParent(top, false);
            avatar = avatarObject.AddComponent<Image>();
            avatar.raycastTarget = false;
            var avatarElement = avatarObject.AddComponent<LayoutElement>();
            avatarElement.minWidth = avatarElement.preferredWidth = AvatarSize;
            avatarElement.minHeight = avatarElement.preferredHeight = AvatarSize;
            initial = Label(avatarObject.transform, "", 34);
            Destroy(initial.GetComponent<LayoutElement>());
            Stretch(initial.rectTransform);
            message = Label(top, "", 24);
            message.alignment = TextAnchor.MiddleLeft;

            note = Label(panel.transform, "", 18);
            Transform buttons = Row(panel.transform, 44f, 10f);
            Button(buttons, "Join", Join);
            Button(buttons, "Decline", Hide);
        }

        private Transform Row(Transform parent, float height, float spacing)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = layout.childForceExpandWidth = false;
            go.AddComponent<LayoutElement>().minHeight = height;
            return go.transform;
        }

        private Text Label(Transform parent, string text, int size)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.color = DarkText;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.supportRichText = true;
            label.text = text;
            go.AddComponent<LayoutElement>();
            return label;
        }

        private void Button(Transform parent, string text, System.Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = buttonSprite;
            image.type = buttonSprite ? Image.Type.Sliced : Image.Type.Simple;
            image.color = ButtonColor;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = 150f; element.preferredHeight = 42f;
            Text label = Label(go.transform, text, 22);
            Destroy(label.GetComponent<LayoutElement>());
            Stretch(label.rectTransform);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        /// <summary>The game's popup panel and menu button sprites and font, as VotePanel uses them.</summary>
        private void FindGameStyle()
        {
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            foreach (ModalOverlay popup in Resources.FindObjectsOfTypeAll<ModalOverlay>())
            {
                foreach (Image image in popup.GetComponentsInChildren<Image>(true))
                {
                    var rect = image.rectTransform;
                    if (image.sprite && image.type == Image.Type.Sliced && image.sprite.border.sqrMagnitude > 0f && rect.rect.width > 200f && rect.rect.height > 100f)
                    { panelSprite = image.sprite; break; }
                }
                if (panelSprite) break;
            }
            foreach (MainMenu menu in Resources.FindObjectsOfTypeAll<MainMenu>())
            {
                var prefab = Traverse.Create(menu).Field<ButtonWidget>("textButtonPrefab").Value;
                if (!prefab) continue;
                if (prefab.label && prefab.label.font) font = prefab.label.font;
                foreach (Image image in prefab.GetComponentsInChildren<Image>(true))
                    if (image.sprite) { buttonSprite = image.sprite; break; }
                break;
            }
        }
    }
}
