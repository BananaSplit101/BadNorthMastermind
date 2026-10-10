using Steamworks;
namespace BadNorthMP.Net
{
    internal static class Roles
    {
        private static SteamSession Session => SteamSession.Instance;
        public static CSteamID MastermindId => Session.InLobby ? Session.Owner : CSteamID.Nil;
        public static CSteamID DefenderId
        {
            get
            {
                if (!Session.InLobby) return CSteamID.Nil;
                if (PartyFlow.AuthorityId != 0) return new CSteamID(PartyFlow.AuthorityId);
                foreach (CSteamID member in Session.Members())
                    if (member != Session.Owner) return member;
                return CSteamID.Nil;
            }
        }
        public static bool HasMastermind => Session.InLobby && Updater.Compatible && DefenderId != CSteamID.Nil;
        public static bool IsMastermind => Session.InLobby && Session.Me == MastermindId;
        // Only this defender simulates and saves the shared campaign.
        public static bool IsDefender => Session.InLobby && Session.Me == DefenderId;
        public static bool IsRemoteDefender => Session.InLobby && !IsMastermind && !IsDefender;
        public static bool WatchesBattle => IsMastermind || IsRemoteDefender;
        public static string RoleOf(CSteamID member) => member == MastermindId ? "Mastermind (host)" : member == DefenderId ? "Defender (campaign leader)" : "Defender";
    }
}
