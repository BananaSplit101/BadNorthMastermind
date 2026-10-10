using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthMP.Game
{
    /// <summary>Leader side: landing the mastermind's commanders on the current island and carrying out their orders.</summary>
    internal static class CommanderBattle
    {
        public static bool TryDeploy(int id, int tile, out string message)
        {
            Island island = GameAccess.CurrentIsland;
            if (!Economy.Active || !island || island.state != Island.State.Playing || Economy.GaveUp)
            { message = "commanders can only join a battle in progress"; return false; }
            if (!CustomWaves.CanLaunch(out message)) return false;
            VikingCommander commander = MastermindSession.Commander(id);
            if (!CommanderRoster.CanDeploy(commander, Economy.DeployedCommanders.Keys, out message)) return false;
            int beach = BeachChoices.Get(island).FindIndex(b => b.tileIndex == tile);
            if (beach < 0)
            { message = "pick a landing tile first"; return false; }
            VikingReference reference = Reference(island, commander.type);
            if (!reference)
            { message = $"{VikingCommanders.TypeName(commander.type)}s can't land on this island"; return false; }
            var crew = new List<KeyValuePair<VikingReference, int>> { new KeyValuePair<VikingReference, int>(reference, VikingCommanders.Crew(commander.type)) };
            if (!CustomWaves.TryLaunch(island, beach, crew, EconomyConfig.MaxShipsPerCommander, out message, out int count, out Wave wave)) return false;
            Squad squad = wave && wave.shipGroups.Count > 0 ? wave.shipGroups[0].squad : null;
            Economy.DeployedCommanders[id] = squad;
            CommanderRoster.MarkDeployed(commander);
            MastermindUpgradeSave.Save();
            if (squad)
            {
                foreach (Agent agent in squad.agents) VikingCommandOrder.Install(agent);
                VikingTraits.Apply(squad, (VikingTrait)commander.trait);
            }
            Net.DefenderTracker.SendCommanders();
            string where = BeachChoices.Get(island)[beach].label;
            string trait = VikingTraits.Name(commander.trait);
            ModLog.Info($"Commander {id} lands {VikingCommanders.Count(count, commander.type)} at the {where} beach");
            string described = (trait.Length > 0 ? trait + " " : "") + "Viking commander";
            string article = "AEIOU".IndexOf(described[0]) >= 0 ? "an" : "a";
            Net.MultiplayerGame.Instance.Announce($"The mastermind has sent {article} {described} with {VikingCommanders.Count(count, commander.type)}!");
            message = $"Commander {id} sails with {VikingCommanders.Count(count, commander.type)}";
            return true;
        }

        /// <summary>The island's own reference for the type, or any loaded one with an agent prefab.</summary>
        private static VikingReference Reference(Island island, int type)
        {
            VikingReference found = Economy.AvailableTypes(island).Find(v => (int)v.type == type);
            if (found) return found;
            foreach (VikingReference candidate in Resources.FindObjectsOfTypeAll<VikingReference>())
                if (candidate && candidate.agent && (int)candidate.type == type) return candidate;
            return null;
        }

        public static bool TryOrder(int id, byte kind, int target, out string message)
        {
            Island island = GameAccess.CurrentIsland;
            if (!Economy.Active || !island || !Economy.DeployedCommanders.TryGetValue(id, out Squad squad))
            { message = "that commander isn't fighting on this island"; return false; }
            if (!squad || !squad.agents.Exists(a => a && a.aliveState != null && a.aliveState.active))
            { message = $"commander {id}'s squad has fallen"; return false; }
            if (kind == VikingCommanders.OrderMove)
            {
                List<NavSpot> spots = island.navSpotter.navSpots;
                if (target < 0 || target >= spots.Count) { message = "unknown tile"; return false; }
                // Like defender squads, two commanders can't hold the same tile.
                foreach (var other in Economy.DeployedCommanders)
                    if (other.Key != id && other.Value && VikingCommandOrder.TargetOf(other.Value) == spots[target])
                    { message = $"commander {other.Key} already holds that tile"; return false; }
                VikingCommandOrder.Move(squad, spots[target]);
                message = $"Commander {id}: moving";
                return true;
            }
            if (kind == VikingCommanders.OrderBurn)
            {
                House[] houses = island.village ? island.village.houses : null;
                if (houses == null || target < 0 || target >= houses.Length || !houses[target] || houses[target].destroyed)
                { message = "that house is already gone"; return false; }
                VikingPatherSquad pather = Pather(squad);
                if (!pather || !pather.house)
                { message = "they can pick a house once they're ashore"; return false; }
                VikingCommandOrder.Release(squad);
                // The squad's burning loop re-reads its house every frame (VikingPatherSquad.HouseBurning).
                Traverse.Create(pather).Property("house").SetValue(houses[target]);
                message = $"Commander {id}: burning a house";
                return true;
            }
            if (kind == VikingCommanders.OrderRelease)
            {
                VikingCommandOrder.Release(squad);
                message = $"Commander {id}: following their own plan";
                return true;
            }
            message = "unknown order";
            return false;
        }

        private static VikingPatherSquad Pather(Squad squad)
        {
            foreach (Agent agent in squad.agents)
            {
                var arsonist = agent ? agent.GetComponent<Arsonist>() : null;
                if (arsonist && arsonist.pather) return arsonist.pather;
            }
            return null;
        }
    }
}
