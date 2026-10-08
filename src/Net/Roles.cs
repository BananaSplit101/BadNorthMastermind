using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Who is the mastermind and who defends. Stored in lobby data by the lobby owner, so both players
    /// always agree. The defender's game runs the real battle; the lobby owner can be either role.
    /// </summary>
    internal static class Roles
    {
        private const string MastermindKey = "bnmp_mastermind";
        private const string ModeKey = "bnmp_mastermind_mode"; // "pick" or "random"

        private static SteamSession Session => SteamSession.Instance;

        public static bool IsRandom => Session.InLobby && Session.GetLobbyData(ModeKey) == "random";

        /// <summary>The mastermind, or Nil when nobody else is in the lobby.</summary>
        public static CSteamID MastermindId
        {
            get
            {
                if (!Session.InLobby)
                    return CSteamID.Nil;
                List<CSteamID> members = Session.Members();
                if (members.Count < 2)
                    return CSteamID.Nil;

                if (ulong.TryParse(Session.GetLobbyData(MastermindKey), out ulong stored))
                {
                    var id = new CSteamID(stored);
                    if (members.Contains(id))
                        return id;
                }
                return DefaultMastermind(members);
            }
        }

        /// <summary>The player whose game runs the battle (two-player lobbies).</summary>
        public static CSteamID DefenderId
        {
            get
            {
                CSteamID mastermind = MastermindId;
                if (mastermind == CSteamID.Nil)
                    return Session.InLobby ? Session.Me : CSteamID.Nil;
                foreach (CSteamID member in Session.Members())
                {
                    if (member != mastermind)
                        return member;
                }
                return CSteamID.Nil;
            }
        }

        public static bool HasMastermind => MastermindId != CSteamID.Nil;
        public static bool IsMastermind => HasMastermind && MastermindId == Session.Me;
        public static bool IsDefender => Session.InLobby && !IsMastermind;

        public static string RoleOf(CSteamID member) => member == MastermindId ? "Mastermind" : "Defender";

        /// <summary>Lobby owner only: step through "each player" then "Random" (which rolls immediately).</summary>
        public static void CycleChoice()
        {
            if (!Session.IsHost)
                return;
            List<CSteamID> members = Session.Members();
            if (members.Count < 2)
                return;

            // Order: guests first (the usual choice), then the host, then Random.
            var options = new List<CSteamID>();
            foreach (CSteamID m in members)
                if (m != Session.Me) options.Add(m);
            options.Add(Session.Me);

            int current = IsRandom ? options.Count : options.IndexOf(MastermindId);
            int next = (current + 1) % (options.Count + 1);
            if (next == options.Count)
            {
                Store(members[Random.Range(0, members.Count)], "random");
            }
            else
            {
                Store(options[next], "pick");
            }
        }

        /// <summary>Lobby owner only: re-roll or fall back when the stored mastermind left.</summary>
        public static void OnMembersChanged()
        {
            if (!Session.IsHost)
                return;
            List<CSteamID> members = Session.Members();
            if (members.Count < 2)
                return;
            if (ulong.TryParse(Session.GetLobbyData(MastermindKey), out ulong stored) && members.Contains(new CSteamID(stored)))
                return;
            if (IsRandom)
                Store(members[Random.Range(0, members.Count)], "random");
            else
                Store(DefaultMastermind(members), "pick");
        }

        private static CSteamID DefaultMastermind(List<CSteamID> members)
        {
            CSteamID owner = Session.Owner;
            foreach (CSteamID m in members)
                if (m != owner) return m;
            return CSteamID.Nil;
        }

        private static void Store(CSteamID mastermind, string mode)
        {
            Session.SetLobbyData(MastermindKey, mastermind.m_SteamID.ToString());
            Session.SetLobbyData(ModeKey, mode);
            ModLog.Info($"Mastermind is now {SteamSession.NameOf(mastermind)} ({mode})");
        }
    }
}
