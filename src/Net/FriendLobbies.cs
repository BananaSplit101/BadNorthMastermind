using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Friends' Bad North Multiplayer games, to join from the Multiplayer screen without an invite. Steam says which
    /// lobby each friend playing Bad North is in (the mod's lobbies are friends-only, so friends may see and join
    /// them); the lobby's own data, kept up to date by its host (Publish), arrives a moment after asking for it.
    /// </summary>
    internal static class FriendLobbies
    {
        public const string VersionKey = "bnmp_version", HostKey = "bnmp_host", PlayersKey = "bnmp_players", StateKey = "bnmp_state";

        public sealed class Game
        {
            public CSteamID lobby;
            public readonly List<CSteamID> friends = new List<CSteamID>();
            public string version, host, state;
            public int players;
            public bool Full => players >= SteamSession.MaxMembers;
            public string HostName => !string.IsNullOrEmpty(host) ? host : SteamSession.NameOf(friends[0]);
        }

        /// <summary>Friends' games found by the last look (only the mod's lobbies, once their data has arrived).</summary>
        public static readonly List<Game> Games = new List<Game>();
        private static readonly Dictionary<CSteamID, float> askedAt = new Dictionary<CSteamID, float>();
        private static float nextLook;

        /// <summary>While the Multiplayer screen is open outside a lobby: look again every couple of seconds.</summary>
        public static void Refresh()
        {
            var session = SteamSession.Instance;
            if (!session.Ready || session.InLobby || Time.unscaledTime < nextLook) return;
            nextLook = Time.unscaledTime + 2f;
            Games.Clear();
            AppId_t app = SteamUtils.GetAppID();
            int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < count; i++)
            {
                CSteamID friend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                CSteamID lobby = LobbyOf(friend, app);
                if (!lobby.IsValid()) continue;
                Game game = Games.Find(g => g.lobby == lobby);
                if (game == null) Games.Add(game = new Game { lobby = lobby });
                game.friends.Add(friend);
            }
            foreach (Game game in Games)
            {
                if (!askedAt.TryGetValue(game.lobby, out float at) || Time.unscaledTime - at > 15f)
                {
                    askedAt[game.lobby] = Time.unscaledTime;
                    SteamMatchmaking.RequestLobbyData(game.lobby);
                }
                game.version = SteamMatchmaking.GetLobbyData(game.lobby, VersionKey);
                game.host = SteamMatchmaking.GetLobbyData(game.lobby, HostKey);
                game.state = SteamMatchmaking.GetLobbyData(game.lobby, StateKey);
                int.TryParse(SteamMatchmaking.GetLobbyData(game.lobby, PlayersKey), out game.players);
            }
            // Lobbies without the mod's data aren't ours (or haven't answered yet: shown at the next look).
            Games.RemoveAll(game => string.IsNullOrEmpty(game.version));
        }

        private static CSteamID LobbyOf(CSteamID friend, AppId_t app)
        {
            if (!SteamFriends.GetFriendGamePlayed(friend, out FriendGameInfo_t played) || played.m_gameID.AppID() != app) return CSteamID.Nil;
            if (played.m_steamIDLobby.IsValid()) return played.m_steamIDLobby;
            // The mod also puts its lobby in the friend's rich presence (what Steam's own "Join game" uses).
            const string prefix = "+connect_lobby ";
            string connect = SteamFriends.GetFriendRichPresence(friend, "connect");
            return connect != null && connect.StartsWith(prefix) && ulong.TryParse(connect.Substring(prefix.Length), out ulong id)
                ? new CSteamID(id) : CSteamID.Nil;
        }

        /// <summary>What the lobby is doing, for its listing.</summary>
        public static string Describe(Game game)
        {
            string doing = game.Full ? "full"
                : game.state == "campaign" ? "campaign under way: join to fight from the next island"
                : game.state == "draft" ? "choosing commanders"
                : "waiting to start";
            string players = game.players > 0 ? $"{game.players}/{SteamSession.MaxMembers} players, " : "";
            string version = game.version != Plugin.Version ? $" (they have {game.version}, you have {Plugin.Version})" : "";
            return $"{game.HostName}'s game: {players}{doing}{version}";
        }

        // ---- Host: what friends see about this lobby --------------------------------------------------

        private static string publishedHost, publishedPlayers, publishedState;
        private static float nextPublish;

        /// <summary>Host, every frame in a lobby: keep the lobby's listing data current (written only when it changes).</summary>
        public static void Publish()
        {
            var session = SteamSession.Instance;
            if (!session.IsHost)
            {
                publishedHost = publishedPlayers = publishedState = null;
                return;
            }
            if (Time.unscaledTime < nextPublish) return;
            nextPublish = Time.unscaledTime + 1f;
            string host = SteamFriends.GetPersonaName();
            string players = session.Members().Count.ToString();
            string state = !PartyFlow.Active ? "open" : PartyFlow.State.phase == 1 ? "draft" : "campaign";
            if (host != publishedHost) session.SetLobbyData(HostKey, publishedHost = host);
            if (players != publishedPlayers) session.SetLobbyData(PlayersKey, publishedPlayers = players);
            if (state != publishedState) session.SetLobbyData(StateKey, publishedState = state);
        }
    }
}
