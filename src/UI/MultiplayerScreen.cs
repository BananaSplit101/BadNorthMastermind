using System;
using System.Collections.Generic;
using System.Text;
using BadNorthMP.Net;
using BadNorthMP.Spectator;
using HarmonyLib;
using I2.Loc;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    /// <summary>
    /// The multiplayer screen, built on the game's own popup (ModalOverlay) so it looks and navigates like
    /// the rest of Bad North. Opened from the Multiplayer button in the main menu and pause menu.
    /// </summary>
    internal class MultiplayerScreen : MonoBehaviour
    {
        private const float RefreshSeconds = 0.25f;
        // Any real term works: the label is replaced with plain text right after.
        private const string PlaceholderTerm = "UI/COMMON/OK";

        private static MultiplayerScreen instance;

        private ModalOverlay overlay;
        private Text messageText;
        private string shownLayout;
        private float nextRefresh;
        private static bool autoOpenPending;
        public static void RequestOpen() { autoOpenPending = true; }

        private static SteamSession Session => SteamSession.Instance;
        private static MultiplayerGame Game => MultiplayerGame.Instance;

        private void Awake() => instance = this;

        /// <summary>Button callback for the menu entries. Returns true so the game plays its click sound.</summary>
        public static bool Open()
        {
            if (PartyFlow.Active && PartyFlow.State.phase == 1 && Updater.Compatible)
            { CommanderDraftScreen.Open(); return true; }
            if (instance && !instance.overlay)
                instance.Show();
            return true;
        }

        public static void CloseForBattle()
        {
            autoOpenPending = false;
            if (!instance || !instance.overlay) return;
            var popup = instance.overlay;
            instance.overlay = null;
            popup.Dismiss();
        }

        private void Update()
        {
            if (autoOpenPending && Session.InLobby && (!SpectatorSession.Active || PartyFlow.State.phase == 1))
            {
                foreach (MainMenu menu in Resources.FindObjectsOfTypeAll<MainMenu>())
                    if (menu.gameObject.activeInHierarchy)
                    { autoOpenPending = false; Open(); break; }
            }
            if (!Session.InLobby) autoOpenPending = false;
            if (!overlay || Time.unscaledTime < nextRefresh)
                return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;

            if (LayoutKey() != shownLayout)
            {
                // Different set of buttons needed (hosted, joined, role changed...): swap in a fresh popup.
                ModalOverlay old = overlay;
                overlay = null;
                old.Dismiss();
                Show();
                return;
            }

            FriendLobbies.Refresh();
            messageText.text = BuildBody();
        }

        private void Show()
        {
            if (PartyFlow.Active && PartyFlow.State.phase == 1 && Updater.Compatible)
            { CommanderDraftScreen.Open(); return; }
            shownLayout = LayoutKey();

            ModalOverlay popup = ModalOverlay.GetInstance();
            overlay = popup;
            messageText = GameUi.InitPopup(popup, "Multiplayer " + Plugin.Version, BuildBody());

            // A newer version on GitHub can be installed from here at any time.
            if (Updater.Staged) AddButton("Install and restart", Updater.Install);
            else if (Updater.UpdateAvailable && !Updater.Busy) AddButton("Update to " + Updater.LatestVersion, Updater.ConfirmDownload);

            bool versionMismatch = Session.InLobby && !Updater.Compatible;
            if (!versionMismatch && Spectator.SpectatorSession.Active)
                AddButton("Stop watching", Spectator.SpectatorSession.Instance.StopWatching);
            else if (Session.Ready && !Session.InLobby && !InCampaign())
                GameUi.AddPopupButton(popup, "Play", () => { overlay = null; CampaignScreen.Open(); }, closes: true);

            if (Session.InLobby && Updater.Compatible && !SpectatorSession.Active && (!InCampaign() || PartyFlow.State.phase == 1))
            {
                if (Session.IsHost && !PartyFlow.Active && Roles.HasMastermind)
                {
                    AddButton("Start", PartyCampaignPicker.Open);
                }
                if (Session.IsHost && PartyFlow.State.phase == 1) AddButton("Back", PartyFlow.CancelDraft);
                if (PartyFlow.State.phase == 1) AddButton("Choose commanders", CommanderDraftScreen.Open);

            }

            if (Session.Ready && !Session.InLobby)
            {
                AddButton("Host game", Session.Host);
                // Friends' games, joined like an accepted invite.
                int shown = 0;
                foreach (FriendLobbies.Game game in FriendLobbies.Games)
                {
                    if (game.Full || shown++ >= 4) continue;
                    CSteamID lobby = game.lobby;
                    AddButton("Join " + game.HostName, () => Session.Join(lobby));
                }
            }
            else if (Session.InLobby)
            {
                if (Session.IsHost)
                {
                    AddButton("Invite friend", Session.OpenInviteDialog);

                }

                AddButton("Leave lobby", Session.Leave);
            }

            popup.AddCancelButton();
            popup.SetCloseAction(() =>
            {
                if (overlay == popup)
                    overlay = null;
            });
        }

        private static string LayoutKey()
        {
            if (!Session.Ready)
                return "wait";
            string update = $"{Updater.UpdateAvailable}/{Updater.Busy}/{Updater.Staged}/";
            if (Session.InLobby && !Updater.Compatible)
                return update + "mismatch";
            if (Spectator.SpectatorSession.Active)
                return update + "spectating";
            if (!Session.InLobby)
            {
                // The Join buttons follow the friends' games found.
                var games = new StringBuilder();
                foreach (FriendLobbies.Game game in FriendLobbies.Games) games.Append(game.lobby.m_SteamID).Append(game.Full ? "f," : ",");
                return update + "idle/" + games;
            }
            return update + $"{(Session.IsHost ? "host" : "guest")}/{Session.Members().Count}/{(Roles.IsMastermind ? "mm" : Roles.IsDefender ? "leader" : "remote")}/{InCampaign()}/{PartyFlow.State.phase}/{PartyFlow.State.token}";
        }

        private Widget AddButton(string label, Action action) => GameUi.AddPopupButton(overlay, label, action);

        /// <summary>Already in a campaign (map or battle): Play only makes sense from the main menu.</summary>
        private static bool InCampaign()
        {
            var campaigns = Singleton<Voxels.TowerDefense.CampaignManager>.instance;
            return campaigns && campaigns.campaign;
        }

        private static string BuildBody()
        {
            if (!Session.Ready)
                return "Waiting for Steam...";

            var sb = new StringBuilder();
            if (Updater.Busy || Updater.Staged)
                sb.AppendLine(Updater.Status).AppendLine();
            else if (Updater.UpdateAvailable)
                sb.AppendLine($"Version {Updater.LatestVersion} is available (you have {Plugin.Version}).").AppendLine();
            if (!Session.InLobby)
            {
                if (Session.Status != "Not connected")
                    sb.AppendLine(Session.Status).AppendLine();
                sb.AppendLine("Host a game and invite your friends: the host is the mastermind who sends the Vikings, everyone else defends the islands together.");
                sb.AppendLine();
                if (FriendLobbies.Games.Count == 0)
                    sb.Append("No friends are in a game right now. Their games show up here to join, or accept their invite in Steam.");
                else
                {
                    sb.AppendLine("Friends' games:");
                    foreach (FriendLobbies.Game game in FriendLobbies.Games) sb.AppendLine(FriendLobbies.Describe(game));
                }
                return sb.ToString();
            }

            foreach (CSteamID member in Session.Members())
            {
                string role = Session.Members().Count > 1 ? Roles.RoleOf(member) : "Waiting for a friend";
                string ping = member == Session.Me ? "you" : Game.PingText(member);
                sb.AppendLine($"{SteamSession.NameOf(member)}  -  {role}  ({ping})");
            }
            sb.AppendLine();
            if (PartyFlow.Active && PartyFlow.State.phase < 3 && Updater.Compatible)
            {
                sb.AppendLine(PartyFlow.Status).AppendLine();
                foreach (var seat in PartyFlow.State.seats)
                {
                    sb.Append(SteamSession.NameOf(new CSteamID(seat.player))).Append(": ");
                    for (int i = 0; i < seat.choices.Length; i++)
                    { if (i > 0) sb.Append(" + "); sb.Append(PartyFlow.ChoiceName(seat.choices[i])); }
                    sb.AppendLine(seat.ready ? "  (Ready)" : "  (Choosing)");
                }
                sb.AppendLine().Append("All new squads start as militia. Classes and abilities are purchased later with campaign gold.");
                return sb.ToString();
            }
            if (MultiplayerGame.HostLoading) sb.AppendLine("The host is loading the live battle...");

            if (!Updater.Compatible)
            {
                sb.AppendLine("Everyone needs the same version to play. " + Updater.Status);
                return sb.ToString();
            }

            if (!Roles.HasMastermind)
                sb.AppendLine("Invite a friend to play.");
            else if (Roles.IsMastermind)
                sb.AppendLine(SpectatorSession.ViewingCampaign
                    ? "You're viewing the defenders' campaign. The coin opens your war chest: upgrade your Vikings and recruit commanders. The defenders vote on the next island."
                    : SpectatorSession.Active
                        ? "You're watching the battle. Plan your waves at the bottom of the screen; the coin at the top right opens the war chest."
                        : PartyFlow.Deployed ? "The defender's campaign map and live battles open here automatically, with your army controls."
                            : "Press Start to choose a multiplayer save or create a new campaign. Defenders then choose their commanders.");
            else if (Roles.IsRemoteDefender && PartyFlow.Deployed && PartyFlow.Mine == null)
                sb.AppendLine(PartyFlow.State.seats.Count >= 4
                    ? "The campaign is under way and the party is full (four defenders), so you're watching."
                    : "The campaign is under way: you join the defenders the next time the party is on the campaign map, and fight from the next island.");
            else if (Roles.IsRemoteDefender)
                sb.AppendLine("The campaign map and battles open automatically. Click an island to vote where to go next; in battle, select your own squad and click a tile to move.");
            else
                sb.AppendLine(PartyFlow.Deployed ? "Click an island to vote where to go next. Each defender controls their own squad; the mastermind sends the Vikings." : "Wait for the mastermind to press Start, then choose your commanders.");

            foreach (string line in Game.RecentEvents(3))
            {
                // Role announcements describe past states; the player list above is authoritative.
                if (line == "You are the mastermind." || line.StartsWith("You defend.")) continue;
                sb.AppendLine().Append(line);
            }
            return sb.ToString();
        }
    }
}
