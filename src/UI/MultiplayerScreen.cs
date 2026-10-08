using System;
using System.Collections.Generic;
using System.Text;
using BadNorthMP.Net;
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
        // Any real term works: the label is overwritten with plain text right after.
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
                // Different set of buttons needed (hosted, joined, left...): swap in a fresh popup.
                ModalOverlay old = overlay;
                overlay = null;
                old.Dismiss();
                Show();
                return;
            }

            messageText.text = BuildBody();
            foreach (var entry in dynamicLabels)
                entry.Key.SetNonLocalizedLabel(entry.Value());
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

            if (!Session.Ready)
            {
                // Nothing to do until Steam is up.
            }
            else if (!Session.InLobby)
            {
                AddButton("Host game", Session.Host);
            }
            else if (Session.IsHost)
            {
                AddButton("Invite friend", Session.OpenInviteDialog);
                AddDynamicButton(() => "Mastermind controls waves: " + (Game.mastermindControlEnabled ? "On" : "Off"),
                    () => Game.mastermindControlEnabled = !Game.mastermindControlEnabled);
                AddButton("Leave lobby", Session.Leave);
            }
            else
            {
                AddButton("Launch next wave", Game.RequestWave);
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
            if (!Session.InLobby)
                return "idle";
            return Session.IsHost ? "host" : "guest";
        }

        // ModalOverlay closes itself when a button's action returns true. Return false to keep the screen
        // open, and use the click sound as the "fail" sound so it still sounds like a normal press.
        private Widget AddButton(string label, Action action)
        {
            return overlay.AddButton(PlaceholderTerm, () =>
                {
                    action();
                    return false;
                })
                .SetNonLocalizedLabel(label)
                .SetFailAudio(FabricID.uiButtonClick)
                .SetHoverAudio(FabricID.uiHover);
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
                sb.AppendLine("Host a game and invite a friend. You defend the islands; your friend is the mastermind who decides when the Viking waves strike.");
                sb.AppendLine();
                sb.Append("To join a friend, accept their invite in Steam.");
                return sb.ToString();
            }

            CSteamID owner = Session.Owner;
            foreach (CSteamID member in Session.Members())
            {
                string role = member == owner ? "Defender" : "Mastermind";
                string ping = member == Session.Me ? "you" : Game.PingText(member);
                sb.AppendLine($"{SteamSession.NameOf(member)}  -  {role}  ({ping})");
            }
            sb.AppendLine();

            if (Session.IsHost)
            {
                if (Session.Members().Count < 2)
                    sb.AppendLine("Waiting for a mastermind to join...");
                else
                    sb.AppendLine(MultiplayerGame.MastermindControlsWaves
                        ? "Your mastermind decides when the waves come."
                        : "Waves arrive on the normal timer.");
            }
            else
            {
                sb.AppendLine(DescribeHostBattle());
            }

            foreach (string line in Game.RecentEvents(3))
                sb.AppendLine().Append(line);
            return sb.ToString();
        }

        private static string DescribeHostBattle()
        {
            if (!Game.HasHostStatus)
                return "Waiting for the host...";
            IslandStatus s = Game.HostStatus;
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
