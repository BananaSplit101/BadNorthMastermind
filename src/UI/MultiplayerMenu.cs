using BadNorthMP.Net;
using Steamworks;
using UnityEngine;

namespace BadNorthMP.UI
{
    /// <summary>"Multiplayer" button in the top-right corner that opens the lobby window.</summary>
    internal class MultiplayerMenu : MonoBehaviour
    {
        private const int WindowId = 0x4E4D50; // "BNMP"
        private const float Width = 340f;

        private bool open;
        private Rect windowRect;
        private string chatDraft = "";

        private static SteamSession Session => SteamSession.Instance;
        private static MultiplayerGame Game => MultiplayerGame.Instance;

        private void OnGUI()
        {
            if (!open)
            {
                string label = Session.InLobby ? $"Multiplayer ({Session.Members().Count})" : "Multiplayer";
                if (GUI.Button(new Rect(Screen.width - 150, 10, 140, 30), label))
                {
                    open = true;
                    windowRect = new Rect(Screen.width - Width - 10, 10, Width, 10);
                }
                return;
            }

            windowRect = GUILayout.Window(WindowId, windowRect, DrawWindow, "Bad North Multiplayer " + Plugin.Version);
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label(Session.Status);

            if (!Session.Ready)
            {
                CloseButton();
                return;
            }

            if (!Session.InLobby)
            {
                GUILayout.Label("Host a game, then invite a friend. You defend; they're the mastermind who sends the waves.");
                if (GUILayout.Button("Host game", GUILayout.Height(30)))
                    Session.Host();
                GUILayout.Label("To join: accept a friend's Steam invite.");
                CloseButton();
                return;
            }

            DrawPlayers();
            GUILayout.Space(6);
            if (Session.IsHost)
                DrawHostControls();
            else
                DrawMastermindControls();
            GUILayout.Space(6);
            DrawChat();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Leave"))
                Session.Leave();
            CloseLabel("Close");
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        private void DrawPlayers()
        {
            GUILayout.Label("Players:");
            CSteamID owner = Session.Owner;
            foreach (CSteamID member in Session.Members())
            {
                string role = member == owner ? "Defender (host)" : "Mastermind";
                string ping = member == Session.Me ? "you" : Game.PingText(member);
                GUILayout.Label($"  {SteamSession.NameOf(member)} - {role} - {ping}");
            }
            if (Session.IsHost && GUILayout.Button("Invite friend", GUILayout.Height(30)))
                Session.OpenInviteDialog();
        }

        private void DrawHostControls()
        {
            Game.mastermindControlEnabled = GUILayout.Toggle(Game.mastermindControlEnabled,
                " Mastermind controls when waves launch");
            if (Game.mastermindControlEnabled && Session.Members().Count < 2)
                GUILayout.Label("  (normal wave timer runs until a mastermind joins)");
        }

        private void DrawMastermindControls()
        {
            if (!Game.HasHostStatus)
            {
                GUILayout.Label("Waiting for the host...");
                return;
            }

            IslandStatus s = Game.HostStatus;
            if (!s.onIsland)
            {
                GUILayout.Label("Host isn't in a battle right now.");
                return;
            }

            GUILayout.Label($"Host's battle: {s.squadsAlive} squads alive, {s.vikingsAlive} vikings on the island");
            bool allLaunched = s.nextWave > s.waveCount;
            if (allLaunched)
                GUILayout.Label($"All {s.waveCount} waves launched.");
            else if (s.waveLaunching)
                GUILayout.Label($"Wave {s.nextWave}/{s.waveCount} is landing...");
            else
                GUILayout.Label(s.mastermindControlsWaves
                    ? $"Wave {s.nextWave}/{s.waveCount} is ready. The Vikings wait for your order."
                    : $"Wave {s.nextWave}/{s.waveCount} arrives on the timer in {s.timeToNextWave:0}s");

            GUI.enabled = !allLaunched && !s.waveLaunching;
            if (GUILayout.Button("Launch next wave", GUILayout.Height(36)))
                Game.RequestWave();
            GUI.enabled = true;
        }

        private void DrawChat()
        {
            foreach (string line in Game.Events)
                GUILayout.Label(line);

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("bnmp_chat");
            chatDraft = GUILayout.TextField(chatDraft, 200);
            bool enterPressed = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return
                                && GUI.GetNameOfFocusedControl() == "bnmp_chat";
            if (GUILayout.Button("Send", GUILayout.Width(60)) || enterPressed)
            {
                Game.SendChat(chatDraft);
                chatDraft = "";
            }
            GUILayout.EndHorizontal();
        }

        private void CloseButton()
        {
            CloseLabel("Close");
            GUI.DragWindow();
        }

        private void CloseLabel(string label)
        {
            if (GUILayout.Button(label))
            {
                open = false;
                windowRect.height = 10; // let GUILayout re-fit next time it opens
            }
        }
    }
}
