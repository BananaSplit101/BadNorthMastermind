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

        public static void Bind(ConfigFile config)
        {
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
