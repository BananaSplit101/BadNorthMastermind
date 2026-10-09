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
        private readonly List<KeyValuePair<Widget, Func<string>>> dynamicLabels = new List<KeyValuePair<Widget, Func<string>>>();

        private static SteamSession Session => SteamSession.Instance;
        private static MultiplayerGame Game => MultiplayerGame.Instance;

        private void Awake() => instance = this;

        /// <summary>Button callback for the menu entries. Returns true so the game plays its click sound.</summary>
        public static bool Open()
        {
            if (instance && !instance.overlay)
                instance.Show();
            return true;
        }

        private void Update()
        {
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

            messageText.text = BuildBody();
            foreach (var entry in dynamicLabels)
                entry.Key.label.text = entry.Value();
        }

        private void Show()
        {
            dynamicLabels.Clear();
            shownLayout = LayoutKey();

            ModalOverlay popup = ModalOverlay.GetInstance();
            overlay = popup;
            popup.InitializeNonLocalized("Multiplayer", BuildBody(), cancelable: false);
            // Pooled popups keep their Localize components; stop them from overwriting our text.
            var traverse = Traverse.Create(popup);
            traverse.Field<Localize>("titleLocalize").Value.enabled = false;
            traverse.Field<Localize>("messageLocalize").Value.enabled = false;
            messageText = traverse.Field<Text>("message").Value;

            if (Spectator.SpectatorSession.Active)
                AddButton("Stop watching", Spectator.SpectatorSession.Instance.Stop);

            if (Session.Ready && !Session.InLobby)
            {
                AddButton("Host game", Session.Host);
            }
            else if (Session.InLobby)
            {
                if (Session.IsHost)
                {
                    AddButton("Invite friend", Session.OpenInviteDialog);
                    if (Session.Members().Count > 1)
                        AddDynamicButton(MastermindChoiceLabel, Roles.CycleChoice);
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
            if (Spectator.SpectatorSession.Active)
                return "spectating";
            if (!Session.InLobby)
                return "idle";
            return $"{(Session.IsHost ? "host" : "guest")}/{Session.Members().Count}/{(Roles.IsMastermind ? "mm" : "def")}";
        }

        private static string MastermindChoiceLabel()
        {
            string name = SteamSession.NameOf(Roles.MastermindId);
            if (Roles.MastermindId == Session.Me)
                name = "You";
            return Roles.IsRandom ? $"Mastermind: Random ({name})" : $"Mastermind: {name}";
        }

        // ModalOverlay closes itself when a button's action returns true. Return false to keep the screen
        // open, and use the click sound as the "fail" sound so it still sounds like a normal press.
        private Widget AddButton(string label, Action action)
        {
            Widget widget = overlay.AddButton(PlaceholderTerm, () =>
                {
                    action();
                    return false;
                })
                .SetFailAudio(FabricID.uiButtonClick)
                .SetHoverAudio(FabricID.uiHover);
            return GameUi.SetPlainLabel(widget, label);
        }

        private void AddDynamicButton(Func<string> label, Action action)
        {
            Widget widget = AddButton(label(), action);
            dynamicLabels.Add(new KeyValuePair<Widget, Func<string>>(widget, label));
        }

        private static string BuildBody()
        {
            if (!Session.Ready)
                return "Waiting for Steam...";

            var sb = new StringBuilder();
            if (!Session.InLobby)
            {
                if (Session.Status != "Not connected")
                    sb.AppendLine(Session.Status).AppendLine();
                sb.AppendLine("Host a game and invite a friend. One of you defends the islands; the other is the mastermind who decides when and where the Vikings strike.");
                sb.AppendLine();
                sb.Append("To join a friend, accept their invite in Steam.");
                return sb.ToString();
            }

            foreach (CSteamID member in Session.Members())
            {
                string role = Session.Members().Count > 1 ? Roles.RoleOf(member) : "Waiting for a friend";
                string ping = member == Session.Me ? "you" : Game.PingText(member);
                sb.AppendLine($"{SteamSession.NameOf(member)}  -  {role}  ({ping})");
            }
            sb.AppendLine();

            if (!Roles.HasMastermind)
                sb.AppendLine("Invite a friend to play.");
            else if (Roles.IsMastermind)
                sb.AppendLine(SpectatorSession.Active
                    ? "You're watching the battle. Build your waves in the panel on the right."
                    : "Stay on the main menu: the defender's battle opens here automatically, with your army panel.");
            else
                sb.AppendLine("Start or continue a campaign and pick an island. Your mastermind sends the Vikings: there are no normal waves while they're connected.");

            foreach (string line in Game.RecentEvents(3))
                sb.AppendLine().Append(line);
            return sb.ToString();
        }

        private static string DescribeDefenderBattle()
        {
            if (!Game.HasDefenderStatus)
                return "Waiting for the defender...";
            IslandStatus s = Game.DefenderStatus;
            if (!s.onIsland)
                return "The defender isn't in a battle yet.";

            string forces = $"{s.squadsAlive} squads defending, {s.vikingsAlive} Vikings ashore.";
            if (s.nextWave > s.waveCount)
                return $"All {s.waveCount} waves launched. {forces}";
            if (s.waveLaunching)
                return $"Wave {s.nextWave} of {s.waveCount} is landing. {forces}";
            return s.mastermindControlsWaves
                ? $"Wave {s.nextWave} of {s.waveCount} awaits your order. {forces}"
                : $"Wave {s.nextWave} of {s.waveCount} arrives in {s.timeToNextWave:0}s. {forces}";
        }
    }
}
