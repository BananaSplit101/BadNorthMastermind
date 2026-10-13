using BepInEx.Configuration;

namespace BadNorthMP
{
    /// <summary>Viking trait strength, as in GMW's Viking Commander mod (it scales every buff and nerf).</summary>
    public enum TraitStrength { Easy, Normal, Hard, VeryHard, Impossible }

    /// <summary>Player options (BepInEx/config/com.bananasplit.badnorthmp.cfg). Gameplay ones count on the campaign leader.</summary>
    internal static class ModConfig
    {
        public static ConfigEntry<TraitStrength> TraitDifficulty;
        public static ConfigEntry<bool> SailIn, SailInCamera, SailInBars, SailInWarCry;
        public static ConfigEntry<float> SailInBoatSpeed;
        public static ConfigEntry<bool> HideEnemyLineup, ShowUnitIntegrity;
        public static ConfigEntry<int> MaxDeployed;
        public static ConfigEntry<float> PerExtraDefender;
        public static ConfigEntry<bool> SendReports, ReportsNoticeShown;
        public static ConfigEntry<string> ReportInstallId, ReportToken;

        public static void Bind(ConfigFile config)
        {
            SendReports = config.Bind("Diagnostics", "Send error reports", true,
                "Send error logs automatically to help fix bugs (PostHog): the mod's log, errors, and what the game was doing, " +
                "with Steam names, Steam IDs and your Windows user name taken out. When something goes wrong, everyone in the " +
                "lobby sends their recent log under one code. Off: nothing is sent.");
            ReportsNoticeShown = config.Bind("Diagnostics", "Notice shown", false,
                "Whether you've been told about error reports (a popup the first time you press Play). Nothing is sent before.");
            ReportInstallId = config.Bind("Diagnostics", "Install id", "",
                "A random id for this install, not your Steam account: it ties your reports together.");
            ReportToken = config.Bind("Diagnostics", "Project token", "",
                "Where error reports go (a PostHog project token). Empty: the mod's own project.");
            TraitDifficulty = config.Bind("Viking commanders", "Trait strength", TraitStrength.Normal,
                "How strongly a Viking commander's trait buffs (and nerfs) its squad. Used by the campaign leader.");
            SailIn = config.Bind("Commanders sail in", "Enabled", true,
                "Deployed commanders arrive by longship at the nearest beach instead of appearing; village commanders walk out of a house. Used by the campaign leader.");
            SailInBoatSpeed = config.Bind("Commanders sail in", "Boat speed", 0.85f,
                "Speed of the commanders' boats (1 = normal). Used by the campaign leader.");
            SailInCamera = config.Bind("Commanders sail in", "Cinematic camera", true,
                "Your camera follows each commander's boat to shore.");
            SailInBars = config.Bind("Commanders sail in", "Cinematic bars", true,
                "Black bars while the camera follows a boat.");
            SailInWarCry = config.Bind("Commanders sail in", "War cry", true,
                "A war cry when a commander's boat sets off.");
            MaxDeployed = config.Bind("Party", "Max deployed commanders", 6, new ConfigDescription(
                "How many squads the party brings to an island (the game's own limit is 4); the rest of the commanders stay " +
                "in reserve. An island's rescued commander counts as one. Used by the campaign leader.",
                new AcceptableValueRange<int>(1, 10)));
            // 1.0.7's key holds its old 0.25 in everyone's file: drop it, so the gentler default below takes over.
            var oldPerExtraDefender = new ConfigDefinition("Party", "Vikings per extra defender");
            config.Bind(oldPerExtraDefender, 0f);
            config.Remove(oldPerExtraDefender);
            PerExtraDefender = config.Bind("Party", "Viking strength per extra defender", Game.EconomyConfig.DefaultPerExtraDefender, new ConfigDescription(
                "How much stronger the Vikings get for each defender beyond the first: every wave's budget and the mastermind's " +
                "island payout grow by this share (0.1: two defenders 1.1x, three 1.2x, four 1.3x; 0 turns it off). Used by the campaign leader.",
                new AcceptableValueRange<float>(0f, 1f)));
            HideEnemyLineup = config.Bind("Interface", "Hide enemy lineup", true,
                "In multiplayer, defenders don't see which Vikings are coming (map, island intro and results). The mastermind always does.");
            ShowUnitIntegrity = config.Bind("Interface", "Unit integrity", true,
                "A selected squad's banner shows how many soldiers are left.");
        }

        public static float TraitFactor()
        {
            switch (TraitDifficulty?.Value ?? TraitStrength.Normal)
            {
                case TraitStrength.Easy: return 0.5f;
                case TraitStrength.Hard: return 1.5f;
                case TraitStrength.VeryHard: return 2.5f;
                case TraitStrength.Impossible: return 5f;
                default: return 1f;
            }
        }
    }
}
