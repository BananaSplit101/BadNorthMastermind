using BadNorthMP.Net;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;

namespace BadNorthMP.UI
{
    /// <summary>
    /// A Steam invite offered in game, so nobody has to open the Steam overlay: a card at the right edge, above every
    /// other screen, with Join / Decline. A newer invite replaces it; it goes after a minute, or once we're in that
    /// lobby (also when the invite was accepted through Steam).
    /// </summary>
    internal class InvitePopup : MonoBehaviour
    {
        private const float Lifetime = 60f, AvatarSize = 64f;

        private GameObject canvasObject;
        private Image avatar;
        private Text initial, message, note;
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

        private void Build()
        {
            canvasObject = GameStyle.Canvas("BNMP Invite Canvas", 31500); // above everything, the loading gate (31000) included
            RectTransform panel = GameStyle.Panel(canvasObject.transform, new RectOffset(18, 18, 14, 14), 10f);
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 0.72f);
            panel.pivot = new Vector2(1f, 0.5f);
            panel.anchoredPosition = new Vector2(-24f, 0f);

            Transform top = GameStyle.Row(panel, AvatarSize, 14f);
            var avatarObject = new GameObject("Avatar", typeof(RectTransform));
            avatarObject.transform.SetParent(top, false);
            avatar = avatarObject.AddComponent<Image>();
            avatar.raycastTarget = false;
            var avatarElement = avatarObject.AddComponent<LayoutElement>();
            avatarElement.minWidth = avatarElement.preferredWidth = AvatarSize;
            avatarElement.minHeight = avatarElement.preferredHeight = AvatarSize;
            initial = GameStyle.Label(avatarObject.transform, "", 34);
            Destroy(initial.GetComponent<LayoutElement>());
            GameStyle.Stretch(initial.rectTransform);
            message = GameStyle.Label(top, "", 24);
            message.alignment = TextAnchor.MiddleLeft;

            note = GameStyle.Label(panel, "", 18);
            Transform buttons = GameStyle.Row(panel, 44f, 10f);
            GameStyle.Button(buttons, "Join", Join, 150f);
            GameStyle.Button(buttons, "Decline", Hide, 150f);
        }
    }
}
