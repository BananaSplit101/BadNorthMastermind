using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;
using Voxels.TowerDefense.Upgrades;

namespace BadNorthMP.Game
{
    /// <summary>
    /// Fleeing on beached Viking longships in a multiplayer battle (leader side).
    ///
    /// Natively every ship is spawned up front and Island.PlayIsland sets up their evacuation spots, and each
    /// squad's EvacuateAbility caches the island's landings when it spawns. Mastermind ships arrive later, so
    /// both steps are repeated here. Boats are found by scanning the landings directly: EvacuateAbility's own
    /// UpdateTargets also clears boat highlights and plays a sound, which mustn't happen on the leader's screen
    /// for a teammate's squad.
    /// </summary>
    internal static class Evacuation
    {
        /// <summary>A newly spawned mastermind ship: make it a boat, and let every squad see it.</summary>
        public static void AddShip(Raid raid, Longship ship)
        {
            if (!ship || !ship.evacuationLocation) return;
            // Same as Island.PlayIsland, which also sets up ships while they're still offshore.
            ship.evacuationLocation.Setup();
            Landing[] landings = raid.landingContainer.GetComponentsInChildren<Landing>(true);
            foreach (Squad squad in GameAccess.PlayerSquads)
            {
                var english = squad as EnglishSquad;
                var ability = english && english.upgradeManager ? english.upgradeManager.GetUpgrade<EvacuateAbility>() : null;
                if (ability) Traverse.Create(ability).Field("landings").SetValue(landings);
            }
        }

        private static float SquadArea(EvacuateAbility ability) => Traverse.Create(ability).Method("GetSquadArea").GetValue<float>();

        /// <summary>Beached, empty boats big enough for this squad, nearest first; also whether any boat at all is free.</summary>
        public static List<SquadEvacuationLocation> Boats(EnglishSquad squad, out bool anyFree)
        {
            anyFree = false;
            var result = new List<SquadEvacuationLocation>();
            Island island = GameAccess.CurrentIsland;
            var ability = squad.upgradeManager.GetUpgrade<EvacuateAbility>();
            if (!island || !island.raid || !ability || !squad.heroAgent) return result;
            float area = SquadArea(ability);
            foreach (Landing landing in island.raid.landingContainer.GetComponentsInChildren<Landing>(true))
            {
                Longship ship = landing.spawnedShip;
                if (!ship || !ship.landed || !ship.evacuationLocation) continue;
                anyFree |= ship.evacuationLocation.IsAvailable();
                if (ship.evacuationLocation.IsAvailable(area)) result.Add(ship.evacuationLocation);
            }
            Vector3 hero = squad.heroAgent.transform.position;
            result.Sort((a, b) => (a.transform.position - hero).sqrMagnitude.CompareTo((b.transform.position - hero).sqrMagnitude));
            return result;
        }

        /// <summary>Whether the squad could flee right now (what its Evacuate button should show).</summary>
        public static bool CanFlee(EnglishSquad squad)
        {
            var ability = squad.upgradeManager ? squad.upgradeManager.GetUpgrade<EvacuateAbility>() : null;
            return ability && !ability.isBanned && !ability.IsBlocked() && Boats(squad, out bool unused).Count > 0;
        }

        /// <summary>Leader: send a squad to a boat (the nearest suitable one when none is given).</summary>
        public static bool Order(EnglishSquad squad, SquadEvacuationLocation boat, out string result)
        {
            var ability = squad.upgradeManager.GetUpgrade<EvacuateAbility>();
            if (!ability) { result = "This squad can't flee."; return false; }
            if (ability.isBanned) { result = "This commander can't flee from here."; return false; }
            if (ability.IsBlocked()) { result = "Your squad is busy and can't flee right now."; return false; }
            List<SquadEvacuationLocation> boats = Boats(squad, out bool anyFree);
            if (boat && !boats.Contains(boat)) boat = null;
            if (!boat && boats.Count > 0) boat = boats[0];
            if (!boat)
            {
                result = anyFree ? "No beached boat is big enough for your squad." : "No boat is beached and empty yet.";
                return false;
            }
            // SelectLocation deselects whatever the leader has selected; give it back afterwards.
            var selector = Singleton<SquadSelector>.instance;
            EnglishSquad kept = selector ? selector.selectedSquad : null;
            Traverse.Create(ability).Method("SelectLocation", boat).GetValue();
            if (kept && kept != squad && kept.selectable && selector && !selector.selectedSquad) selector.SelectSquad(kept);
            result = "Fleeing to a boat.";
            return true;
        }
    }
}
