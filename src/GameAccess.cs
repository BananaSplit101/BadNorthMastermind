using System.Collections.Generic;
using Voxels.TowerDefense;

namespace BadNorthMP
{
    /// <summary>Helpers for reaching into the game's island and squad state.</summary>
    internal static class GameAccess
    {
        /// <summary>The island currently loaded for gameplay, or null on menus and the campaign map.</summary>
        public static Island CurrentIsland
        {
            get
            {
                var manager = Singleton<IslandGameplayManager>.instance;
                return manager ? manager.island : null;
            }
        }

        public static bool IsPlaying
        {
            get
            {
                var island = CurrentIsland;
                return island && island.state == Island.State.Playing;
            }
        }

        public static List<Squad> PlayerSquads
        {
            get
            {
                var island = CurrentIsland;
                return island && island.english ? island.english.livingSquads : new List<Squad>();
            }
        }
    }
}
