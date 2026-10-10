using System.Collections.Generic;
using HarmonyLib;
using Voxels.TowerDefense;
namespace BadNorthMP.Game
{
    internal static class CustomShipOrders
    {
        public static void Assign(Longship ship)
        {
            foreach (Agent agent in ship.agents)
            {
                var pirate = agent.GetComponent<Pirate>();
                if (!pirate || !agent.brain) throw new System.InvalidOperationException("Ship crew is missing its disembark AI");
                var orders = Traverse.Create(agent.brain).Field<List<IAgentOrder>>("orderList").Value;
                if (!orders.Contains(pirate)) orders.Insert(0, pirate);
                if (!agent.brain.actions.Contains(pirate)) agent.brain.actions.Insert(0, pirate);
                // PickNewOrder retains KillAllEnemies chosen before AddToLongship.
                // Pirate releases control normally when it removes the agent from the ship.
                agent.brain.order = pirate;
                agent.brain.orderMono = pirate;
            }
        }
    }
}
