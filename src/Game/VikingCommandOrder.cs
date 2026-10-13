using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Game
{
    /// <summary>
    /// "Go to this tile" for a mastermind commander's Vikings (leader side), moving like defender squads
    /// (EnglishPatherAgent + EnglishFormationAgent): follow the tile's path, then step into a formation slot and face
    /// outward. The Swordsman combat AI only chases enemies near the ordered destination, so ordering a squad away makes
    /// it disengage. Arrival (standing still once in place, so no shoving and twitching), unsticking and torch rules come
    /// from ABaLaQiYaShanMaiI's BadNorthNewMode (GroupOrder, MIT). An order given on the ship is followed once they land.
    /// </summary>
    internal sealed class VikingCommandOrder : MonoBehaviour, IAgentOrder
    {
        private const float SettledMovability = 0.05f;

        private Agent agent;
        private Pirate pirate;
        private Arsonist arsonist;
        public NavSpot target;
        private NavPos slot;
        private Vector3 slotWorld;
        private bool hasSlot, settled;
        private bool arsonChecked, arsonWasActive;
        private Vector3 lastPosition;
        private float stuckTime, bypassUntil;

        /// <summary>Put the order first in the Viking's list; it stays idle until given a tile.</summary>
        public static void Install(Agent agent)
        {
            if (!agent || !agent.brain || agent.GetComponent<VikingCommandOrder>()) return;
            List<IAgentOrder> orders = Traverse.Create(agent.brain).Field<List<IAgentOrder>>("orderList").Value;
            if (orders == null) return;
            var order = agent.gameObject.AddComponent<VikingCommandOrder>();
            order.agent = agent;
            order.pirate = agent.GetComponent<Pirate>();
            order.arsonist = agent.GetComponent<Arsonist>();
            orders.Insert(0, order);
        }

        /// <summary>The tile the squad was ordered to, or null when it follows its own plan.</summary>
        public static NavSpot TargetOf(Squad squad)
        {
            foreach (Agent agent in squad.agents)
            {
                var order = agent ? agent.GetComponent<VikingCommandOrder>() : null;
                if (order && order.target) return order.target;
            }
            return null;
        }

        public static void Move(Squad squad, NavSpot spot)
        {
            var orders = new List<VikingCommandOrder>();
            foreach (Agent agent in squad.agents)
            {
                var order = agent && agent.aliveState != null && agent.aliveState.active ? agent.GetComponent<VikingCommandOrder>() : null;
                if (order) orders.Add(order);
            }
            if (orders.Count == 0) return;
            // One slot per living Viking, spaced like a defender squad (NavSpotFormationSquad.UpdateFormation);
            // where the tile is too cramped, slots are pushed apart like the game's SlotPusher does.
            var formation = new SquadFormation(orders.Count, spot);
            var slots = new NavPos[orders.Count];
            var world = new Vector3[orders.Count];
            bool cramped = false;
            for (int i = 0; i < orders.Count; i++)
            {
                NavPos navPos = spot.navPos;
                if (!navPos.Move(formation.Get(i) * orders[i].agent.radius * 1.01f * 2f)) cramped = true;
                slots[i] = navPos;
                world[i] = navPos.valid ? navPos.wPos : spot.navPos.wPos;
            }
            if (cramped) PushApart(orders, world);
            for (int i = 0; i < orders.Count; i++)
            {
                VikingCommandOrder order = orders[i];
                order.target = spot;
                order.slot = slots[i].valid ? slots[i] : spot.navPos;
                order.slotWorld = world[i];
                order.hasSlot = true;
                order.settled = false;
                order.stuckTime = 0f;
                order.bypassUntil = 0f;
                // Native orders keep control while they want it (Brain.PickNewOrder), so take it explicitly.
                if (order.WantsControl()) { order.agent.brain.order = order; order.agent.brain.orderMono = order; }
            }
        }

        /// <summary>Overlapping slots each move 20% of the overlap apart until none overlap (the game's SlotPusher).</summary>
        private static void PushApart(List<VikingCommandOrder> orders, Vector3[] world)
        {
            for (int iteration = 0; iteration < 200; iteration++)
            {
                bool moved = false;
                for (int i = 0; i < orders.Count; i++)
                    for (int j = i + 1; j < orders.Count; j++)
                    {
                        float need = (orders[i].agent.radius + orders[j].agent.radius) * 1.01f;
                        Vector3 apart = world[i] - world[j];
                        apart.y = 0f;
                        if (apart.sqrMagnitude >= need * need) continue;
                        float length = Mathf.Sqrt(apart.sqrMagnitude);
                        Vector3 direction = length <= 0.0001f ? Vector3.right : apart / length;
                        Vector3 push = direction * (length - need) * 0.2f;
                        world[i] -= push;
                        world[j] += push;
                        moved = true;
                    }
                if (!moved) break;
            }
        }

        /// <summary>Back to the native plan (burning houses, then hunting defenders).</summary>
        public static void Release(Squad squad)
        {
            foreach (Agent agent in squad.agents)
            {
                var order = agent ? agent.GetComponent<VikingCommandOrder>() : null;
                if (!order) continue;
                order.target = null;
                order.settled = false;
                order.RestoreArson();
            }
        }

        public bool WantsControl() => target && agent && (!pirate || !pirate.longship);

        public void SampleOrder(NavPos navPos, ref Vector3 dir, ref float dist)
        {
            if (!target || !navPos.valid) { dir = Vector3.zero; dist = 0f; return; }
            ((IPathTarget)target).SampleDistanceDir(navPos, ref dir, ref dist);
            if (hasSlot && Time.time >= bypassUntil)
            {
                Vector3 toSlot = slotWorld - agent.wPos;
                toSlot.y = 0f;
                float slotDistance = toSlot.magnitude;
                // Arrived: close enough to the tile counts, since a crowd keeps outer slots occupied.
                if (settled)
                {
                    if (slotDistance >= 0.8f && dist >= 0.5f) settled = false; // only pushed well away starts them again
                }
                else if (dist < 0.25f || (dist < 0.6f && slotDistance < 0.45f))
                    settled = true;
                if (settled) { dir = Vector3.zero; dist = 0f; }
                // EnglishFormationAgent.ModifyPath: straight to the own slot only when it's in plain reach.
                else if (slotDistance > 0.05f && (dist < 1.5f || slotDistance < 1.5f) && slot.valid && navPos.TriCast(slot))
                {
                    dir = toSlot / slotDistance;
                    dist = slotDistance;
                }
            }
            else if (Time.time < bypassUntil && dist >= 0.8f && dir.sqrMagnitude > 0.0001f)
            {
                // Unsticking: a little sideways, around whoever blocks the way.
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                if (side.sqrMagnitude > 0.0001f) dir = dir.normalized + side.normalized * 0.35f;
            }
            if (!settled && dir.sqrMagnitude < 0.0001f)
            {
                // The path field gives nothing right at awkward edges: walk straight at the tile rather than stand.
                Vector3 straight = target.navPos.wPos - agent.wPos;
                straight.y = 0f;
                if (straight.sqrMagnitude > 0.0001f) { dir = straight.normalized; dist = Mathf.Max(dist, straight.magnitude); }
            }
            TrackStuck(dist);
            // The torch thrower reads "close to the order" as "at my house"; give it the real distance to the house.
            float houseDistance = HouseDistance(navPos);
            if (dist < 0.2f) dist = houseDistance;
            UpdateArson(houseDistance);
        }

        public void ApplyOrder()
        {
            ApplyWalk();
            // In place: face out from the tile like a defender squad.
            if (settled) agent.LookInDirection(target.lookDir);
            else agent.LookInDirection(agent.orderDir, 720f, 20f);
        }

        public void ApplyWalk()
        {
            if (settled) { agent.walkDir = Vector3.zero; agent.movability = SettledMovability; return; }
            agent.walkDir = agent.orderDir;
            // Next to a house orderDist is the (large) house distance, so clamp before blending.
            agent.movability = Mathf.Lerp(2f, 0.2f, Mathf.Clamp01(agent.orderDist));
        }

        /// <summary>Stuck for 1.5 s far from the tile: sidestep for 0.8 s. Not near the tile (a crowd there is fine).</summary>
        private void TrackStuck(float dist)
        {
            if (dist < 0.8f) { stuckTime = 0f; bypassUntil = 0f; return; }
            Vector3 position = agent.wPos;
            float moved = (position - lastPosition).magnitude;
            lastPosition = position;
            stuckTime = moved < 0.002f ? stuckTime + Time.deltaTime : 0f;
            if (stuckTime > 1.5f) { stuckTime = 0f; bypassUntil = Time.time + 0.8f; }
        }

        /// <summary>How far the Viking is from its squad's house to burn (VikingPatherSquad.SampleOrder's measure).</summary>
        private float HouseDistance(NavPos navPos)
        {
            House house = arsonist && arsonist.pather ? arsonist.pather.house : null;
            if (!house || !house.intact || house.distanceField == null || !navPos.valid) return 1f;
            float distance = house.distanceField.SampleDistance(navPos);
            float heightBonus = ExtraMath.RemapValue(Mathf.Abs(navPos.pos.y - house.transform.position.y), 0.2f, 0f);
            return Mathf.Max(0f, distance - heightBonus);
        }

        /// <summary>The torch thrower stays among the Viking's actions only right next to an intact house.</summary>
        private void UpdateArson(float houseDistance)
        {
            if (!arsonist || agent.brain == null || agent.brain.actions == null) return;
            if (!arsonChecked)
            {
                arsonChecked = true;
                arsonWasActive = agent.brain.actions.Contains(arsonist);
            }
            bool want = arsonWasActive && houseDistance < 0.2f;
            bool has = agent.brain.actions.Contains(arsonist);
            if (want && !has) agent.brain.actions.Add(arsonist);
            else if (!want && has) agent.brain.actions.Remove(arsonist);
        }

        private void RestoreArson()
        {
            if (arsonChecked && arsonWasActive && arsonist && agent && agent.brain != null && agent.brain.actions != null &&
                !agent.brain.actions.Contains(arsonist))
                agent.brain.actions.Add(arsonist);
            arsonChecked = false;
        }
    }
}
