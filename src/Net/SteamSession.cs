using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Steam lobby + P2P transport. Uses the Steamworks.NET bindings compiled into Assembly-CSharp;
    /// the game's own SteamManager initialises Steam and pumps callbacks on the main thread.
    /// </summary>
    internal class SteamSession : MonoBehaviour
    {
        // The game's platform layer uses P2P channels 1-3 (SteamElements); stay clear of them.
        public const int Channel = 7;
        private const int MaxMembers = 5; // one defender, one mastermind for now
        private const string VersionKey = "bnmp_version";
        private const uint EnterResponseSuccess = 1;

        public static SteamSession Instance { get; private set; }

        public CSteamID Lobby { get; private set; } = CSteamID.Nil;
        public bool InLobby => Lobby != CSteamID.Nil;
        public bool IsHost => InLobby && SteamMatchmaking.GetLobbyOwner(Lobby) == Me;
        public CSteamID Me => SteamUser.GetSteamID();
        public bool Ready { get; private set; }
        public string Status { get; private set; } = "Waiting for Steam...";

        public event Action LobbyChanged = delegate { };
        public event Action<CSteamID, byte[]> MessageReceived = delegate { };

        private Callback<LobbyEnter_t> lobbyEnter;
        private Callback<LobbyChatUpdate_t> lobbyChatUpdate;
        private Callback<LobbyDataUpdate_t> lobbyDataUpdate;
        private Callback<GameLobbyJoinRequested_t> lobbyJoinRequested;
        private Callback<GameRichPresenceJoinRequested_t> richPresenceJoinRequested;
        private Callback<P2PSessionRequest_t> p2pSessionRequest;
        private Callback<P2PSessionConnectFail_t> p2pSessionFailed;
        private readonly Dictionary<CSteamID, float> sendFailureLogged = new Dictionary<CSteamID, float>();

        /// <summary>A lobby member's P2P session was dropped and will be renegotiated; resend anything they must have.</summary>
        public event Action<CSteamID> ConnectionReset = delegate { };
        private CallResult<LobbyCreated_t> lobbyCreated;

        private CSteamID pendingLobby = CSteamID.Nil;
        private bool creatingLobby;
        private ulong launchLobby;
        private readonly byte[] receiveBuffer = new byte[64 * 1024];

        private void Awake()
        {
            Instance = this;
            launchLobby = ParseConnectLobby(Environment.GetCommandLineArgs());
            if (launchLobby == 0) launchLobby = Updater.ConsumeRejoin();
        }

        private void Update()
        {
            if (!Ready)
            {
                // Steamworks calls only work after the game's SteamManager has initialised the API.
                if (!CS.Platform.BasePlatformManager.Initialized)
                    return;
                RegisterCallbacks();
                Ready = true;
                Status = "Not connected";
                ModLog.Info($"Steam ready as {SteamFriends.GetPersonaName()}");

                if (launchLobby != 0)
                {
                    // Game was launched by accepting an invite while it wasn't running.
                    ModLog.Info($"Joining lobby {launchLobby} from launch command line");
                    Join(new CSteamID(launchLobby));
                }
            }

            ReceivePackets();
        }

        private void OnApplicationQuit() => Leave();

        private void RegisterCallbacks()
        {
            lobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            lobbyDataUpdate = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
            lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnLobbyJoinRequested);
            richPresenceJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(OnRichPresenceJoinRequested);
            p2pSessionRequest = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);
            p2pSessionFailed = Callback<P2PSessionConnectFail_t>.Create(OnP2PSessionFailed);
            lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
        }

        // ---- Lobby actions -------------------------------------------------------------------

        public void Host()
        {
            if (!Ready || creatingLobby || InLobby || pendingLobby != CSteamID.Nil)
                return;
            creatingLobby = true;
            Status = "Creating lobby...";
            lobbyCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, MaxMembers));
        }

        public void Join(CSteamID lobby)
        {
            if (!Ready || lobby == CSteamID.Nil || lobby == Lobby || lobby == pendingLobby)
                return;
            if (creatingLobby)
                return;
            Leave();
            pendingLobby = lobby;
            Status = "Joining lobby...";
            SteamMatchmaking.JoinLobby(lobby);
        }

        /// <summary>Opens the Steam overlay's invite dialog for the current lobby.</summary>
        public void OpenInviteDialog()
        {
            if (InLobby)
                SteamFriends.ActivateGameOverlayInviteDialog(Lobby);
        }

        public void Leave() => Leave("Not connected");

        public void Leave(string status)
        {
            Status = status;
            pendingLobby = CSteamID.Nil;
            creatingLobby = false;
            if (!InLobby)
                return;

            foreach (CSteamID member in Members())
            {
                if (member != Me)
                    SteamNetworking.CloseP2PSessionWithUser(member);
            }
            SteamMatchmaking.LeaveLobby(Lobby);
            SteamFriends.SetRichPresence("connect", null);
            Lobby = CSteamID.Nil;
            ModLog.Info($"Left lobby: {status}");
            LobbyChanged();
        }

        public List<CSteamID> Members()
        {
            var members = new List<CSteamID>();
            if (!InLobby)
                return members;
            int count = SteamMatchmaking.GetNumLobbyMembers(Lobby);
            for (int i = 0; i < count; i++)
                members.Add(SteamMatchmaking.GetLobbyMemberByIndex(Lobby, i));
            return members;
        }

        public CSteamID Owner => InLobby ? SteamMatchmaking.GetLobbyOwner(Lobby) : CSteamID.Nil;

        public string GetLobbyData(string key) => InLobby ? SteamMatchmaking.GetLobbyData(Lobby, key) : "";

        /// <summary>Only the lobby owner can write lobby data; Steam ignores it from anyone else.</summary>
        public void SetLobbyData(string key, string value)
        {
            if (IsHost)
                SteamMatchmaking.SetLobbyData(Lobby, key, value);
        }

        public static string NameOf(CSteamID user) => SteamFriends.GetFriendPersonaName(user);

        // ---- Messaging -----------------------------------------------------------------------

        public void Send(CSteamID to, byte[] data, bool reliable = true)
        {
            if (!InLobby || to == Me)
                return;
            var mode = reliable ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliableNoDelay;
            if (SteamNetworking.SendP2PPacket(to, data, (uint)data.Length, mode, Channel)) return;
            // At most one line per player every 10 seconds.
            if (sendFailureLogged.TryGetValue(to, out float last) && Time.unscaledTime - last < 10f) return;
            sendFailureLogged[to] = Time.unscaledTime;
            ModLog.Warn($"Steam refused a {data.Length}-byte message to {NameOf(to)}");
        }

        /// <summary>
        /// Drop the P2P session with a player so the next message negotiates a fresh one. Sessions outlive a
        /// player's game restart (an update), and messages sent into the old one are silently lost.
        /// </summary>
        public void ResetConnection(CSteamID user)
        {
            if (user == Me || user == CSteamID.Nil) return;
            SteamNetworking.CloseP2PSessionWithUser(user);
            ConnectionReset(user);
        }

        public void Broadcast(byte[] data, bool reliable = true)
        {
            foreach (CSteamID member in Members())
                Send(member, data, reliable);
        }

        private void ReceivePackets()
        {
            if (!InLobby)
                return;

            while (SteamNetworking.IsP2PPacketAvailable(out uint size, Channel))
            {
                if (!SteamNetworking.ReadP2PPacket(receiveBuffer, (uint)receiveBuffer.Length, out uint read, out CSteamID sender, Channel))
                    break;
                if (!Members().Contains(sender))
                    continue; // only talk to people in our lobby

                var data = new byte[read];
                Array.Copy(receiveBuffer, data, read);
                try
                {
                    MessageReceived(sender, data);
                }
                catch (Exception e)
                {
                    ModLog.Error($"Bad message from {NameOf(sender)}: {e}");
                }
            }
        }

        // ---- Steam callbacks -----------------------------------------------------------------

        private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            creatingLobby = false;
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            {
                Status = $"Couldn't create lobby ({result.m_eResult})";
                ModLog.Error(Status);
                return;
            }
            // The creator also receives LobbyEnter_t, which finishes the setup.
            pendingLobby = new CSteamID(result.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyMemberLimit(pendingLobby, MaxMembers);
            SteamMatchmaking.SetLobbyData(pendingLobby, VersionKey, Plugin.Version);
        }

        private void OnLobbyEnter(LobbyEnter_t result)
        {
            var lobby = new CSteamID(result.m_ulSteamIDLobby);
            if (lobby != pendingLobby && !creatingLobby)
                return; // not a lobby we asked for
            pendingLobby = CSteamID.Nil;
            creatingLobby = false;

            if (result.m_EChatRoomEnterResponse != EnterResponseSuccess)
            {
                Status = result.m_EChatRoomEnterResponse == 4 ? "Lobby full. Ask the host to create a new lobby with this version." : $"Couldn't join lobby (response {result.m_EChatRoomEnterResponse})";
                ModLog.Error(Status);
                return;
            }

            Lobby = lobby;
            SteamMatchmaking.SetLobbyMemberData(lobby, "bnmp_client_version", Plugin.Version);

            // Lets friends use "Join Game" from their Steam friends list.
            SteamFriends.SetRichPresence("connect", "+connect_lobby " + lobby.m_SteamID);
            Status = IsHost ? "Hosting" : $"Connected to {NameOf(Owner)}";
            ModLog.Info($"Entered lobby {lobby.m_SteamID} as {(IsHost ? "host" : "guest")}");
            LobbyChanged();
            UI.MultiplayerScreen.RequestOpen();
        }

        private void OnLobbyChatUpdate(LobbyChatUpdate_t update)
        {
            if (update.m_ulSteamIDLobby != Lobby.m_SteamID)
                return;
            var user = new CSteamID(update.m_ulSteamIDUserChanged);
            bool entered = (update.m_rgfChatMemberStateChange & 1) != 0;
            ModLog.Info($"{NameOf(user)} {(entered ? "joined" : "left")}");
            // A player who left (or restarted to update) must not keep the old session: anything sent into it is lost.
            if (!entered && user != Me) SteamNetworking.CloseP2PSessionWithUser(user);
            LobbyChanged();
        }

        public event Action LobbyDataChanged = delegate { };

        private void OnLobbyDataUpdate(LobbyDataUpdate_t update)
        {
            // Fires for lobby-wide data (not member data) when SteamIDMember == lobby id.
            if (update.m_ulSteamIDLobby == Lobby.m_SteamID && update.m_ulSteamIDMember == Lobby.m_SteamID)
                LobbyDataChanged();
        }

        // Friend accepted an invite (or clicked Join Game) while the game was already running.
        private void OnLobbyJoinRequested(GameLobbyJoinRequested_t request)
        {
            ModLog.Info($"Join request from {NameOf(request.m_steamIDFriend)}");
            Join(request.m_steamIDLobby);
        }

        private void OnRichPresenceJoinRequested(GameRichPresenceJoinRequested_t request)
        {
            ulong lobby = ParseConnectLobby(request.m_rgchConnect.Split(' '));
            if (lobby != 0)
                Join(new CSteamID(lobby));
        }

        private void OnP2PSessionRequest(P2PSessionRequest_t request)
        {
            if (Members().Contains(request.m_steamIDRemote))
                SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote);
        }

        private void OnP2PSessionFailed(P2PSessionConnectFail_t failure)
        {
            CSteamID user = failure.m_steamIDRemote;
            if (!InLobby || !Members().Contains(user)) return; // someone who left: expected
            ModLog.Warn($"Lost the connection to {NameOf(user)} (Steam P2P error {failure.m_eP2PSessionError}); reconnecting");
            ResetConnection(user);
        }

        private static ulong ParseConnectLobby(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "+connect_lobby")
                {
                    try { return ulong.Parse(args[i + 1]); }
                    catch (FormatException) { return 0; }
                    catch (OverflowException) { return 0; }
                }
            }
            return 0;
        }
    }
}
