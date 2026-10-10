using System.Collections.Generic;
using BadNorthMP.Net;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.Ballistics;

namespace BadNorthMP.Game
{
    internal enum UnitUpgrade : byte { Health, Damage, Count }

    internal static class MastermindUpgrades
    {
        public const int TypeCount = 8;
        public const int MaxLevel = 3;
        public static bool ValidType(VikingAgent.Type type) => (int)type >= 0 && (int)type < TypeCount;
        public static bool ValidKind(UnitUpgrade kind) => (int)kind >= 0 && (int)kind < 3;
        public static float Multiplier(int level) => 1f + 0.25f * Mathf.Clamp(level, 0, MaxLevel);
        public static int CrewCount(int paidCount, int level) => paidCount + paidCount * Mathf.Clamp(level, 0, MaxLevel) / 4;
        public static int Cost(VikingReference type, UnitUpgrade kind, int currentLevel) =>
            !type || !ValidKind(kind) || currentLevel < 0 || currentLevel >= MaxLevel ? 0 :
            Cost(Economy.UnitCost(type), kind, currentLevel);
        public static int Cost(int unitCost, UnitUpgrade kind, int currentLevel) =>
            unitCost <= 0 || !ValidKind(kind) || currentLevel < 0 || currentLevel >= MaxLevel ? 0 :
            unitCost * (kind == UnitUpgrade.Count ? 12 + 6 * currentLevel : 8 + 4 * currentLevel);

        /// <summary>
        /// The Viking types the mastermind has unlocked: seen in this campaign or present on an unlocked island, and
        /// in a fight the island's own types too. The same on the map and in battle, so the war chest's locks agree.
        /// </summary>
        public static List<VikingReference> AvailableTypes()
        {
            var result = Economy.Active ? Economy.AvailableTypes(GameAccess.CurrentIsland) : new List<VikingReference>();
            var manager = Singleton<CampaignManager>.instance;
            if (!manager || !manager.campaign || Profile.campaign == null) return result;
            foreach (LevelNode level in manager.campaign.levels)
                if (level)
                    foreach (VikingReference type in level.enemies)
                        if (type && type.agent && ValidType(type.type) &&
                            (level.IsUnlocked() || Profile.campaign.HasEverSeen(type.type)) && !result.Exists(v => v.type == type.type))
                            result.Add(type);
            return result;
        }

        public static bool CanBuy()
        {
            if (!Roles.IsDefender || !Roles.HasMastermind) return false;
            // Upgrades are paid from the war chest, so they stay on sale for the whole fight.
            if (Economy.Active) return !Economy.GaveUp && CustomWaves.CanLaunch(out string ignored);
            var manager = Singleton<CampaignManager>.instance;
            var stack = Singleton<Stack>.instance;
            return manager && manager.campaign && Profile.campaign != null && !Profile.campaign.gameOver &&
                stack && stack.stateCampaign.gameObject.activeInHierarchy && !stack.stateLoading.gameObject.activeInHierarchy && !GameAccess.CurrentIsland &&
                MultiplayerCampaigns.IsMultiplayer(Profile.activeCampaignMeta);
        }

        public static bool TryBuy(long session, int revision, int islandIndex, VikingAgent.Type type,
            UnitUpgrade kind, out string message)
        {
            MastermindUpgradeSave.EnsureCampaign();
            if (!CanBuy())
            { message = "upgrade purchases aren't ready; return to the map or resume the battle"; return false; }
            Island island = GameAccess.CurrentIsland;
            int currentIndex = Economy.Active && island && island.levelNode != null ? island.levelNode.index : -1;
            if (currentIndex != islandIndex ||
                session != MastermindSession.UpgradeSession || revision != MastermindSession.UpgradeRevision)
            { message = "army changed; wait for the latest upgrade state"; return false; }
            if (!ValidType(type) || !ValidKind(kind))
            { message = "unknown unit or upgrade"; return false; }
            VikingReference reference = AvailableTypes().Find(v => v.type == type);
            if (!reference)
            { message = "that Viking type isn't unlocked here"; return false; }
            int level = MastermindSession.UpgradeLevel(type, kind);
            if (level >= MaxLevel)
            { message = "that upgrade is already at its maximum"; return false; }
            int cost = Cost(reference, kind, level);
            if (cost <= 0 || !MastermindSession.TrySpendWarChest(cost))
            { message = "not enough war chest gold for that upgrade"; return false; }
            if (!MastermindSession.AddUpgrade(type, kind))
            {
                MastermindSession.Refund(cost);
                message = "couldn't save the upgrade; gold refunded (see the campaign leader's log)";
                return false;
            }
            if (kind == UnitUpgrade.Health && island && island.vikings)
                foreach (Squad squad in island.vikings.allSquads)
                    if (squad)
                        foreach (Agent agent in squad.agents)
                            if (agent && agent.spawned != null && agent.spawned.active) ApplyHealth(agent);
            message = $"{type}: {kind} upgraded to {level + 1}/{MaxLevel} ({cost} gold)";
            return true;
        }

        internal static bool IsAuthoritative => CustomWaves.MastermindMode && Economy.Active;

        internal static float DamageMultiplier(Agent agent)
        {
            if (!IsAuthoritative || !agent || !agent.faction || agent.faction.side != Faction.Side.Viking) return 1f;
            VikingAgent viking = agent.GetComponent<VikingAgent>();
            return viking && viking.vikingReference
                ? Multiplier(MastermindSession.UpgradeLevel(viking.type, UnitUpgrade.Damage)) : 1f;
        }

        internal static void ApplyHealth(Agent agent)
        {
            if (!IsAuthoritative || !agent || !agent.faction || agent.faction.side != Faction.Side.Viking) return;
            VikingAgent viking = agent.GetComponent<VikingAgent>();
            if (!viking || !viking.vikingReference || agent.health <= 0f) return;
            var stats = agent.GetComponent<MastermindUpgradeHealth>();
            if (!stats) stats = agent.gameObject.AddComponent<MastermindUpgradeHealth>();
            float multiplier = Multiplier(MastermindSession.UpgradeLevel(viking.type, UnitUpgrade.Health));
            float ratio = multiplier / stats.appliedMultiplier;
            agent.maxHealth *= ratio;
            agent.health *= ratio; // Preserve wounds; buying health never refills a damaged unit.
            stats.appliedMultiplier = multiplier;
        }
    }

    internal sealed class MastermindUpgradeHealth : MonoBehaviour
    {
        public float appliedMultiplier = 1f;
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.Spawn))]
    internal static class MastermindUpgradeSpawnPatch
    {
        // Spawn fires the native difficulty callbacks; multiply their final health afterwards.
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Agent __instance) => MastermindUpgrades.ApplyHealth(__instance);
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.DealDamage))]
    internal static class MastermindUpgradeDamagePatch
    {
        private static void Prefix(ref Attack attack)
        {
            // Projectile settings are upgraded once at launch, including multi-hit brute arrows.
            if (!attack.monoAttacker || attack.monoAttacker is Shootable) return;
            Agent source = attack.monoAttacker as Agent;
            var component = attack.monoAttacker as AgentComponent;
            if (!source && component) source = component.agent;
            attack.damage *= MastermindUpgrades.DamageMultiplier(source);
        }
    }

    [HarmonyPatch(typeof(Shootable), nameof(Shootable.Shoot))]
    internal static class MastermindUpgradeProjectilePatch
    {
        private static void Prefix(Agent shooter, ref AttackSettings attackSettings) =>
            attackSettings.damage *= MastermindUpgrades.DamageMultiplier(shooter);
    }

}
