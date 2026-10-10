using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.Upgrades;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace BadNorthMP.Traits
{
    /// <summary>
    /// GMW's Traits Galore (update 3), ported with permission: eight new commander traits. Damage and attack speed act on
    /// every hit and on each class's own timing (TraitDamagePatch, TraitTiming), so they work for infantry too.
    /// </summary>
    internal static class TraitsGalore
    {
        public const string Axe = "Hero_Trait_Axe", Barricade = "Hero_Trait_Barricade", Coward = "Hero_Trait_Coward",
            LightWeapons = "Hero_Trait_Light_Weapons", Mad = "Hero_Trait_Mad", Short = "Hero_Trait_Short", Sick = "Hero_Trait_Sick",
            Unpopular = "Hero_Trait_Unpopular";

        public static void Register()
        {
            CustomTraits.Register<TraitAxe>(Axe, "Axe Squad", "Heavy Axe Attacks, Slower Swings",
                "Equips the squad with heavy axes. They deal massive damage to enemies but require a longer recovery between swings.", "trait_axesquad.png");
            CustomTraits.Register<TraitBarricade>(Barricade, "Barricade", "House Immune While Replenishing",
                "While this squad is replenishing in a house, the house becomes completely immune to damage.", "trait_barricade.png");
            CustomTraits.Register<TraitCoward>(Coward, "Coward", "+15% Speed, Fears Brutes",
                "Flight over fight. +15% Movement Speed, but they are terrified of Brutes and will move erratically if one gets within 3 tiles.", "trait_coward.png");
            CustomTraits.Register<TraitLightWeapons>(LightWeapons, "Light Weapons", "-15% Damage, +30% Attack Speed",
                "Equips the squad with lighter, more agile weapons. They suffer a 15% reduction in damage per hit but benefit from a 30% increase in attack speed.", "trait_lightweapons.png");
            CustomTraits.Register<TraitMad>(Mad, "Mad", "+40% Damage, +50% Resistance, Highly Aggressive",
                "These warriors are completely reckless. They deal 40% more damage and take 50% less, but will ignore movement commands and blindly charge the nearest enemy unless ordered to replenish.", "trait_mad.png");
            CustomTraits.Register<TraitShort>(Short, "Short", "+11 Squad Size, 1 HP, 25% Damage",
                "This squad is vertically challenged. Max squad size is increased by 11. However, they are weak, dealing only 25% damage and dying in a single hit.", "trait_short.png");
            CustomTraits.Register<TraitSick>(Sick, "Sick", "Attacks Inflict a Deadly Infection",
                "This squad is suffering from illness, but their attacks infect enemies, dealing 20% of the enemy's max health as damage per second.", "trait_sick.png");
            CustomTraits.Register<TraitUnpopular>(Unpopular, "Unpopular", "-3 Max Squad Size",
                "This commander is notoriously difficult to work with. Fewer soldiers are willing to join their squad.", "trait_unpopular.png");
        }

        /// <summary>Damage the squad deals (×) for its commander's trait.</summary>
        public static float DamageDealt(string trait) =>
            trait == Axe ? 2f : trait == LightWeapons ? 0.85f : trait == Mad ? 1.4f : trait == Short ? 0.25f : 1f;

        /// <summary>Damage the squad takes (×).</summary>
        public static float DamageTaken(string trait) => trait == Mad ? 0.5f : trait == Short ? 1.5f : 1f;

        /// <summary>Time between the squad's attacks (×): above 1 is slower.</summary>
        public static float AttackTime(string trait) => trait == Axe ? 1.5f : trait == LightWeapons ? 0.77f : 1f;

        /// <summary>The squad's soldiers' movement speed (×).</summary>
        public static float Speed(string trait) => trait == Coward ? 1.15f : 1f;
    }

    // ---- The traits (applied where the squad really spawns: single player, or the campaign leader) -----------------

    internal class TraitAxe : HeroUpgradeDefinition { }
    internal class TraitBarricade : HeroUpgradeDefinition { }
    internal class TraitLightWeapons : HeroUpgradeDefinition { }
    internal class TraitSick : HeroUpgradeDefinition { }

    internal class TraitCoward : HeroUpgradeDefinition
    {
        public override void OnAppliedToSquad(EnglishSquad squad, int upgradeLevel)
        {
            base.OnAppliedToSquad(squad, upgradeLevel);
            squad.gameObject.AddComponent<CowardTraitLogic>().squad = squad;
        }
    }

    internal class TraitMad : HeroUpgradeDefinition
    {
        public override void OnAppliedToSquad(EnglishSquad squad, int upgradeLevel)
        {
            base.OnAppliedToSquad(squad, upgradeLevel);
            squad.gameObject.AddComponent<MadTraitLogic>().squad = squad;
        }
    }

    internal class TraitShort : HeroUpgradeDefinition
    {
        public override void OnAppliedToSquad(EnglishSquad squad, int upgradeLevel)
        {
            base.OnAppliedToSquad(squad, upgradeLevel);
            squad.maxCount += 11;
        }
    }

    internal class TraitUnpopular : HeroUpgradeDefinition
    {
        public override void OnAppliedToSquad(EnglishSquad squad, int upgradeLevel)
        {
            base.OnAppliedToSquad(squad, upgradeLevel);
            squad.maxCount = Mathf.Max(1, squad.maxCount - 3);
        }
    }

    /// <summary>Per-soldier changes for a Traits Galore trait, made once for each soldier as it joins the squad.</summary>
    [HarmonyPatch(typeof(Agent), "FixedUpdateAgent")]
    internal static class GaloreSoldierPatch
    {
        private static void Postfix(Agent __instance)
        {
            if (!TraitRules.Simulates) return;
            var squad = __instance.squad as EnglishSquad;
            string trait = TraitRules.TraitOf(squad);
            if (trait == null || __instance.GetComponent<GaloreSoldier>()) return;
            __instance.gameObject.AddComponent<GaloreSoldier>();
            float speed = TraitsGalore.Speed(trait);
            if (speed != 1f) { __instance.maxSpeed *= speed; __instance.speed *= speed; }
            if (trait == TraitsGalore.Short)
            {
                __instance.scale *= TraitVisuals.ShortScale;
                __instance.maxHealth = 1f;
                __instance.health = 1f;
            }
        }
    }

    internal class GaloreSoldier : MonoBehaviour { }

    /// <summary>Coward: within 3 tiles of a Brute the squad scrambles away from it (not while replenishing indoors).</summary>
    internal class CowardTraitLogic : MonoBehaviour
    {
        public EnglishSquad squad;
        private float nextCheck;

        private void Update()
        {
            if (!squad || !squad.heroAgent || !squad.heroAgent.aliveState.active || Indoors(squad) || Time.time < nextCheck) return;
            nextCheck = Time.time + 0.5f;
            Agent brute = Nearest(squad, 3f, agent => agent.name.IndexOf("Viking_Tank", System.StringComparison.Ordinal) >= 0);
            if (!brute || !squad.navSpotOccupant) return;
            Vector2 jitter = Random.insideUnitCircle * 2.5f;
            Vector3 away = (squad.heroAgent.transform.position - brute.transform.position).normalized * 1.5f;
            NavSpot spot = NavSpot.GetNavSpot(squad.heroAgent.transform.position + away + new Vector3(jitter.x, 0f, jitter.y), false);
            if (spot && squad.navSpotOccupant.navSpot != spot) squad.navSpotOccupant.SetNavSpot(spot, false);
        }

        public static bool Indoors(EnglishSquad squad)
        {
            var replenish = squad.heroAgent.GetComponent<ReplenishComponent>();
            return replenish && replenish.indoors != null && replenish.indoors.active;
        }

        public static Agent Nearest(EnglishSquad squad, float within, System.Func<Agent, bool> which)
        {
            var island = squad.faction ? squad.faction.island : null;
            if (!island || !island.vikings) return null;
            Agent nearest = null;
            float best = within;
            foreach (Agent agent in island.vikings.agents)
            {
                if (!agent || !agent.aliveState.active || !which(agent)) continue;
                float distance = Vector3.Distance(squad.heroAgent.transform.position, agent.transform.position);
                if (distance < best) { best = distance; nearest = agent; }
            }
            return nearest;
        }
    }

    /// <summary>Mad: the squad charges the nearest Viking, whatever it's told, unless it was sent to replenish.</summary>
    internal class MadTraitLogic : MonoBehaviour
    {
        public EnglishSquad squad;
        public bool replenishing;
        private float nextCheck;

        private void Update()
        {
            if (!squad || !squad.heroAgent || !squad.heroAgent.aliveState.active || replenishing || CowardTraitLogic.Indoors(squad) || Time.time < nextCheck) return;
            nextCheck = Time.time + 0.5f;
            Agent target = CowardTraitLogic.Nearest(squad, float.MaxValue, agent => true);
            if (!target || !squad.navSpotOccupant) return;
            NavSpot spot = NavSpot.GetNavSpot(target.transform.position, false);
            if (spot && squad.navSpotOccupant.navSpot != spot) squad.navSpotOccupant.SetNavSpot(spot, false);
        }
    }

    /// <summary>Sick: an infected Viking loses 20% of its max health every second for 5 seconds (hitting again renews it).</summary>
    internal class SickDot : MonoBehaviour
    {
        private Agent target;
        private float duration, tick;

        public static void Infect(Agent agent)
        {
            var dot = agent.GetComponent<SickDot>() ?? agent.gameObject.AddComponent<SickDot>();
            dot.target = agent;
            dot.duration = 5f;
        }

        private void Update()
        {
            if (!target || !target.aliveState.active || (duration -= Time.deltaTime) <= 0f) { Destroy(this); return; }
            if ((tick += Time.deltaTime) < 1f) return;
            tick -= 1f;
            target.DealDamage(new Attack(target.maxHealth * 0.2f, 0f, 0f, Vector3.zero, target.chestPos, this, null, "", null));
            if (!target.aliveState.active) Destroy(this);
        }
    }

    // ---- Barricade (the house can't be harmed while the squad replenishes in it) and Mad (replenishing calms it) ------

    internal class HouseBarricade : MonoBehaviour
    {
        public int squads;
        public static bool Holds(House house) => house && house.GetComponent<HouseBarricade>() is HouseBarricade barricade && barricade.squads > 0;
    }

    [HarmonyPatch(typeof(ReplenishAbility), "SelectLocation")]
    internal static class GaloreReplenishStartPatch
    {
        private static void Postfix(ReplenishAbility __instance, object location)
        {
            if (!TraitRules.Simulates || !__instance.squad) return;
            var mad = __instance.squad.GetComponent<MadTraitLogic>();
            if (mad) mad.replenishing = true;
            if (!TraitRules.Has(__instance.squad, TraitsGalore.Barricade)) return;
            var house = (location as MonoBehaviour) ? ((MonoBehaviour)location).GetComponent<House>() : null;
            if (house) (house.GetComponent<HouseBarricade>() ?? house.gameObject.AddComponent<HouseBarricade>()).squads++;
        }
    }

    [HarmonyPatch(typeof(ReplenishAbility), "OnSquadBeginExit")]
    internal static class GaloreReplenishEndPatch
    {
        private static readonly FieldInfo Location = AccessTools.Field(typeof(ReplenishAbility), "location");

        private static void Prefix(ReplenishAbility __instance)
        {
            if (!TraitRules.Simulates || !__instance.squad) return;
            var mad = __instance.squad.GetComponent<MadTraitLogic>();
            if (mad) mad.replenishing = false;
            if (!TraitRules.Has(__instance.squad, TraitsGalore.Barricade) || Location == null) return;
            var place = Location.GetValue(__instance) as MonoBehaviour;
            var barricade = place ? place.GetComponent<HouseBarricade>() : null;
            if (barricade) barricade.squads--;
        }
    }

    [HarmonyPatch(typeof(House), "TryThrow")]
    internal static class GaloreBarricadeThrowPatch
    {
        private static bool Prefix(House __instance, ref bool __result)
        {
            if (!TraitRules.Simulates || !HouseBarricade.Holds(__instance)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(House), "TorchThrow")]
    internal static class GaloreBarricadeTorchThrowPatch
    {
        private static bool Prefix(House __instance) => !TraitRules.Simulates || !HouseBarricade.Holds(__instance);
    }

    [HarmonyPatch(typeof(House), "TorchLand")]
    internal static class GaloreBarricadeTorchLandPatch
    {
        private static bool Prefix(House __instance) => !TraitRules.Simulates || !HouseBarricade.Holds(__instance);
    }

    // ---- What everyone sees ------------------------------------------------------------------------------------

    /// <summary>
    /// The visible side of the traits, also for watchers' copies of the leader's soldiers (PuppetWorld), which don't run
    /// the traits themselves: Short soldiers are smaller, Mountain ones are giants with the Brute's moves, Fearless
    /// infantry dual-wield without a shield.
    /// </summary>
    internal static class TraitVisuals
    {
        public const float ShortScale = 0.85f;
        private static readonly float[] GiantHeroScale = { 1.2f, 1.3f, 1.4f, 1.5f }, GiantScale = { 1.15f, 1.22f, 1.29f, 1.35f };

        public static void HideShield(Agent agent)
        {
            var shield = agent.GetComponent<Shield>();
            if (shield)
            {
                if (shield.shield) shield.shield.localScale = Vector3.zero;
                shield.enabled = false;
            }
            agent.shield = false;
        }

        /// <summary>Watchers: a copy of one of the leader's soldiers takes its trait's look.</summary>
        public static void ApplyToCopy(Agent agent, EnglishSquad squad, bool commander)
        {
            string trait = TraitRules.TraitOf(squad);
            if (trait == null) return;
            bool infantry = agent.GetComponent<Swordsman>();
            int level = Mathf.Clamp(squad.level, 0, 3);
            if (trait == TraitsGalore.Short) agent.scale *= ShortScale;
            if (trait == VanillaTraitsOverhaul.Mountain)
            {
                agent.scale = (commander ? GiantHeroScale : GiantScale)[level];
                if (infantry) VanillaTraitsOverhaul.ConvertToTank(agent, commander);
            }
            if (trait == VanillaTraitsOverhaul.Fearless && infantry)
            {
                VanillaTraitsOverhaul.ConvertToDualWielder(agent, commander);
                HideShield(agent);
            }
        }
    }
}
