using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;
namespace BadNorthMP.Game
{
    internal class BeachChoice
    {
        public string label;
        public int tileIndex;
        public Vector3 center;
        public List<Beaches.Beach.Pos> positions = new List<Beaches.Beach.Pos>();
    }
    internal static class BeachChoices
    {
        private static Island cachedIsland;
        private static List<BeachChoice> cached = new List<BeachChoice>();
        public static List<BeachChoice> Get(Island island)
        {
            if (island == cachedIsland) return cached;
            cachedIsland = island; cached = new List<BeachChoice>();
            if (!island || !island.beaches || !island.navSpotter) return cached;
            var smallest = island.levelNode.possibleShips.Where(s => s).OrderBy(s => s.area).FirstOrDefault();
            if (!smallest) return cached;
            var points = island.beaches.GetBeachPositions(0.1f);
            var probeObject = new GameObject("BNMP landing validation");
            probeObject.transform.SetParent(island.runContainer, false);
            probeObject.SetActive(false);
            var wave = probeObject.AddComponent<Wave>();
            wave.raid = island.raid; wave.waveStartTime = 0f; wave.timeSpreadGroup = 0f; wave.timeSpreadShip = 0f;
            var groupObject = new GameObject("Group"); groupObject.transform.SetParent(probeObject.transform, false);
            var group = wave.AddShipGroup(groupObject.AddComponent<ShipGroup>());
            var landingObject = new GameObject("Landing"); landingObject.transform.SetParent(groupObject.transform, false);
            var probe = group.AddLanding(landingObject.AddComponent<Landing>());
            probe.Init(island); probe.timeOffset = 0f; probe.shipPrefab = smallest;
            float speed = island.levelNode.diffiucltySettings ? island.levelNode.diffiucltySettings.shipSpeedMultiplier : 1f;
            try
            {
                foreach (NavSpot spot in island.navSpotter.navSpots.OrderBy(s => s.index))
                {
                    if (!spot || !spot.navPos.valid) continue;
                    var choice = new BeachChoice { tileIndex = spot.index, center = spot.navPos.wPos, label = "Tile " + (spot.index + 1) };
                    foreach (var point in points)
                    {
                        if (!point.navPos.valid || point.beach == null || point.beach.puddle) continue;
                        Vector3 delta = point.navPos.wPos - choice.center;
                        if (Mathf.Abs(delta.y) > 0.25f || Mathf.Abs(delta.x) > 0.51f || Mathf.Abs(delta.z) > 0.51f) continue;
                        if (probe.TryPlace(point.navPos, point.dir, speed, new List<Landing>())) choice.positions.Add(point);
                    }
                    choice.positions.Sort((a,b) => (a.navPos.wPos-choice.center).sqrMagnitude.CompareTo((b.navPos.wPos-choice.center).sqrMagnitude));
                    if (choice.positions.Count > 0) cached.Add(choice);
                }
            }
            finally { Object.Destroy(probeObject); }
            return cached;
        }
    }
}
