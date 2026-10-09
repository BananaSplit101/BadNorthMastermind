using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Game
{
    /// <summary>A beach the mastermind can land on.</summary>
    internal class BeachChoice
    {
        public string label;
        public Vector3 center;
        /// <summary>Landing spots on this beach, closest to its middle first.</summary>
        public List<Beaches.Beach.Pos> positions = new List<Beaches.Beach.Pos>();
    }

    /// <summary>
    /// The island's beaches in a stable order. Both players compute this from the same island geometry,
    /// so a beach is sent over the network as its index.
    /// </summary>
    internal static class BeachChoices
    {
        private static Island cachedIsland;
        private static List<BeachChoice> cached = new List<BeachChoice>();

        private static readonly string[] Directions =
            { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

        public static List<BeachChoice> Get(Island island)
        {
            if (island == cachedIsland && cached.Count > 0)
                return cached;
            cachedIsland = island;
            cached = new List<BeachChoice>();
            if (!island || !island.beaches)
                return cached;

            var byBeach = new Dictionary<Beaches.Beach, BeachChoice>();
            foreach (Beaches.Beach.Pos pos in island.beaches.GetBeachPositions(0.1f))
            {
                if (pos.beach == null || !pos.navPos.valid)
                    continue;
                if (!byBeach.TryGetValue(pos.beach, out BeachChoice choice))
                {
                    choice = new BeachChoice();
                    byBeach[pos.beach] = choice;
                    cached.Add(choice);
                }
                choice.positions.Add(pos);
            }

            Vector3 islandCenter = IslandCenter(island);
            var labelCounts = new Dictionary<string, int>();
            foreach (BeachChoice choice in cached)
            {
                Vector3 sum = Vector3.zero;
                foreach (Beaches.Beach.Pos p in choice.positions)
                    sum += p.navPos.wPos;
                choice.center = sum / choice.positions.Count;
                Vector3 c = choice.center;
                choice.positions.Sort((a, b) => (a.navPos.wPos - c).sqrMagnitude.CompareTo((b.navPos.wPos - c).sqrMagnitude));

                Vector3 offset = choice.center - islandCenter;
                float angle = Mathf.Repeat(Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg + 22.5f, 360f);
                string label = Directions[(int)(angle / 45f) % 8];
                labelCounts[label] = labelCounts.TryGetValue(label, out int n) ? n + 1 : 1;
                choice.label = labelCounts[label] > 1 ? $"{label} {labelCounts[label]}" : label;
            }
            return cached;
        }

        private static Vector3 IslandCenter(Island island)
        {
            var spots = island.navSpotter ? island.navSpotter.navSpots : null;
            if (spots == null || spots.Count == 0)
                return Vector3.zero;
            Vector3 sum = Vector3.zero;
            foreach (NavSpot spot in spots)
                sum += spot.navPos.wPos;
            return sum / spots.Count;
        }
    }
}
