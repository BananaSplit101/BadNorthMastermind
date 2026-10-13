using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI.UpgradeScreen;
using Voxels.TowerDefense.Upgrades;
using Random = UnityEngine.Random;

namespace BadNorthMP.Traits
{
    // GMW's Vanilla Traits Overhaul (1.7.9), ported with permission: all 13 of the game's commander traits get new
    // stats and behaviours and new text. Gameplay effects run where battles are simulated (TraitRules.Simulates); the
    // visible parts are applied to watchers' copies too (TraitVisuals).

    internal static class VanillaTraitsOverhaul
    {
        public const string Popular = "Hero_Trait_ExtraUnit", Energetic = "Hero_Trait_ShortCooldown", Sharp = "Hero_Trait_SharpWeapons",
            SureFooted = "Hero_Trait_Sturdy", FleetOfFoot = "Hero_Trait_Fast", Ironskin = "Hero_Trait_ExtraArmor",
            HeavyWeapons = "Hero_Trait_BluntWeapons", RousingSpeeches = "Hero_Trait_FastReplenish", HeavyLoad = "Hero_Trait_ExtraUses",
            Mountain = "Hero_Trait_Giant", Skillful = "Hero_Trait_CheaperSkills", Collector = "Hero_Trait_CheaperItems", Fearless = "Hero_Trait_NoFlee";

        /// <summary>The vanilla traits' new names and text (the definitions keep their ids, so saves are unaffected).</summary>
        public static void Register()
        {
            Text(Popular, "Popular", "Bonus Squad Size & Faster Replenish.", "Loved by the people.\nBolsters the ranks with +3 Squad Size.\nReplenishes fallen soldiers 15% faster.");
            Text(Energetic, "Energetic", "Faster Movement & Shorter Cooldowns.", "Tireless vigor.\nAbility cooldowns are reduced by 50%.\n+20% overall Movement Speed.");
            Text(Sharp, "Sharp Weapons", "High Damage & Less Knockback.", "Honed to a lethal edge.\nDeals 100% Extra Damage.\nDeals 25% less knockback and stun.");
            Text(SureFooted, "Sure-Footed", "Stun Immunity & Heavy Knockback.", "Unshakeable as the earth.\nAll English forces deal 25% extra knockback.\nUnits take 80% less knockback and are entirely immune to stun.");
            Text(FleetOfFoot, "Fleet of Foot", "Swift Movement & Rapid Attacks.", "Swift as the northern winds.\nGrants a passive 57% bonus to Movement Speed.\nAttacks and abilities activate 20% faster.\nStamina regenerates rapidly and costs 50% less.");
            Text(Ironskin, "Ironskin", "High Damage Resistance & Slower Speed.", "Flesh like forged steel.\nGrants a massive 75% Damage Resistance.\nTakes 50% less knockback and stun.\nSuffers -40% Movement Speed and -30% Attack Speed.");
            Text(HeavyWeapons, "Heavy Weapons", "Crushing Knockback & Stun Damage.", "Crushing force behind every blow.\nStrikes inflict +75% Knockback, +60% Stun, and +10% Damage.\nSuffers -20% Movement Speed.");
            Text(RousingSpeeches, "Rousing Speeches", "In-Combat Healing & Faster Replenish.", "Words that mend broken spirits.\nHouse Replenish Time is accelerated by 32%.\nSquad passively regenerates 50% max health per second in combat.");
            Text(HeavyLoad, "Heavy Load", "Bonus Item Usages & Damage Resistance.", "Burdened but prepared.\nGrants +1 Usage on equipped items and +20% Damage Resistance.\nSuffers -25% Movement Speed.");
            Text(Mountain, "Mountain", "Towering Giants That Fight Relentlessly.", "Towering behemoths of war.\nCannot replenish in houses.\nInfantry fight relentlessly.\nSquad gains +50% overall Health, increased armor, and stun resistance.\nScales to bonus damage and bonus knockback.");
            Text(Skillful, "Skillful", "Cheaper Skills & Faster Recharges.", "Mastery of the craft.\nSkill upgrades cost 50% fewer coins.\nSkill abilities recharge 33% faster.");
            Text(Collector, "Collector", "Cheaper Items & Faster Recharges.", "A hoard of tactical wealth.\nItem upgrades cost 50% fewer coins.\nItem abilities recharge 33% faster.");
            Text(Fearless, "Fearless", "Soldiers That Don't Break Under Pressure.", "Unyielding in the face of death.\nCannot flee.\nInfantry dual-wield swords, leap attack, and gain +45% max health per level.\nArchers move unhindered while firing with +30% damage/knockback.\nPikemen will not panic or break formation when engaged.\nImmune to intimidation.");
        }

        private static void Text(string id, string name, string shortText, string description)
        {
            HeroUpgradeDefinition trait = ResourceList<HeroUpgradeDefinition>.Get(id);
            if (!trait) { ModLog.Warn("Vanilla trait not found: " + id); return; }
            string key = "BNMP/VTO/" + id.ToUpperInvariant();
            CustomTraits.Term(key + "/NAME", name);
            CustomTraits.Term(key + "/SHORT", shortText);
            CustomTraits.Term(key + "/DESC", description);
            trait.nameTerm = key + "/NAME";
            trait.shortDescription = key + "/SHORT";
            if (trait.levels != null && trait.levels.Length > 0)
            {
                trait.levels[0].description = key + "/DESC";
                trait.levels[0].prepurchaseDescription = key + "/DESC";
            }
        }

        /// <summary>A defender of a Fearless squad, where battles are simulated.</summary>
        public static bool FearlessDefender(Agent agent) => TraitRules.Simulates && agent.isEnglish && TraitRules.Has(agent.squad, Fearless);

        /// <summary>An infantryman's look and moves become the Viking two-hander's (Fearless): animations, sounds, leap attack.</summary>
        public static void ConvertToDualWielder(Agent agent, bool commander)
        {
            Agent source = VikingClone("Viking_Twohanded");
            if (!source) return;
            if (!commander) CopyAnimator(source, agent, true);
            var from = source.GetComponent<Swordsman>(); var to = agent.GetComponent<Swordsman>();
            if (from && to) { to.swingSound = from.swingSound; to.swordSound = from.swordSound; }
            var jumpFrom = source.GetComponent<JumpAttack>(); var jumpTo = agent.GetComponent<JumpAttack>();
            if (jumpFrom && jumpTo)
                foreach (string field in new[] { "attackSettings", "fabricLaunchID", "fabricLandHitID", "fabricLandMissID", "fabricLandShieldID" })
                {
                    FieldInfo info = AccessTools.Field(typeof(JumpAttack), field);
                    if (info != null) info.SetValue(jumpTo, info.GetValue(jumpFrom));
                }
        }

        /// <summary>An infantryman's look and sounds become the Viking Brute's (Mountain).</summary>
        public static void ConvertToTank(Agent agent, bool commander)
        {
            Agent source = VikingClone("Viking_Tank");
            if (!source) return;
            if (!commander) CopyAnimator(source, agent, false);
            var from = source.GetComponent<Swordsman>(); var to = agent.GetComponent<Swordsman>();
            if (from && to) { to.swingSound = from.swingSound; to.swordSound = from.swordSound; to.chargeSound = from.chargeSound; }
            agent.hurtSound = source.hurtSound;
            var deathFrom = source.GetComponent<Death>(); var deathTo = agent.GetComponent<Death>();
            if (deathFrom && deathTo) deathTo.deathSound = deathFrom.deathSound;
            var bodyFrom = source.GetComponent<Body>(); var bodyTo = agent.GetComponent<Body>();
            if (bodyFrom && bodyTo) bodyTo.baseMoveSoundRef = bodyFrom.baseMoveSoundRef;
        }

        private static Agent VikingClone(string name)
        {
            if (!LevelStateObjectReferences.dict.TryGetValue(name, out var value)) return null;
            var reference = value as VikingReference;
            return reference && reference.vikingClone ? reference.vikingClone.agent : null;
        }

        private static void CopyAnimator(Agent source, Agent agent, bool all)
        {
            Animator from = source.GetComponent<Animator>(), to = agent.GetComponent<Animator>();
            if (!from || !to) return;
            to.runtimeAnimatorController = from.runtimeAnimatorController;
            if (!all) return;
            to.updateMode = from.updateMode;
            to.cullingMode = from.cullingMode;
            to.applyRootMotion = from.applyRootMotion;
        }
    }

    internal class TraitBuffTracker : MonoBehaviour
    {
        public bool processed;
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.Setup))]
    internal static class VtoAgentSetupPatch
    {
        private static void Postfix(Agent __instance)
        {
            if (!TraitRules.Simulates) return;
            var tracker = __instance.GetComponent<TraitBuffTracker>() ?? __instance.gameObject.AddComponent<TraitBuffTracker>();
            tracker.processed = false;
        }
    }

    /// <summary>A soldier's stats for its commander's trait, once (its first update), and the continuous effects.</summary>
    [HarmonyPatch(typeof(Agent), "FixedUpdateAgent")]
    internal static class VtoAgentUpdatePatch
    {
        private static readonly float[] GiantHeroArmor = { 12f, 15f, 18f, 21f }, GiantArmor = { 2f, 2.5f, 3f, 3.5f };
        private static readonly float[] GiantHeroStun = { 0.5f, 0.45f, 0.4f, 0.35f }, GiantStun = { 0.75f, 0.7f, 0.65f, 0.6f };

        private static void Postfix(Agent __instance, float dt)
        {
            if (!TraitRules.Simulates) return;
            var squad = __instance.squad as EnglishSquad;
            if (!squad || squad.hero == null) return;
            string trait = TraitRules.TraitOf(squad);
            if (trait == null) return;
            var tracker = __instance.GetComponent<TraitBuffTracker>();
            if (tracker && !tracker.processed)
            {
                tracker.processed = true;
                Buff(__instance, squad, trait);
            }
            if (trait == VanillaTraitsOverhaul.RousingSpeeches && __instance.aliveState.active)
                __instance.health = Mathf.MoveTowards(__instance.health, __instance.maxHealth, 0.5f * __instance.maxHealth * dt);
            if (__instance.animator)
            {
                if (trait == VanillaTraitsOverhaul.FleetOfFoot) __instance.animator.speed = 1.2f;
                else if (trait == VanillaTraitsOverhaul.Ironskin) __instance.animator.speed = 0.7f;
            }
            if (trait != VanillaTraitsOverhaul.Mountain) return;
            if (__instance == squad.heroAgent)
            {
                var replenish = squad.upgradeManager.GetUpgrade<ReplenishAbility>();
                if (replenish) replenish.BanAbility("Giants cannot replenish", null, null);
            }
            // Giants intimidate whoever they're fighting at close range.
            if (__instance.enemyAgent && __instance.aliveAndGrounded.active)
            {
                Vector3 offset = __instance.enemyAgent.navPos.pos - __instance.navPos.pos;
                float sqr = offset.sqrMagnitude;
                if (sqr <= 1.5f)
                {
                    float reach = __instance.radius + __instance.radius * 0.7f + __instance.enemyAgent.radius;
                    __instance.enemyAgent.Intimidate(ExtraMath.RemapValue(Mathf.Sqrt(sqr), reach, 1f, 1f, 0f), 0f);
                }
            }
        }

        private static void Buff(Agent agent, EnglishSquad squad, string trait)
        {
            bool commander = agent == squad.heroAgent;
            int level = Mathf.Clamp(squad.level, 0, 3);
            if (trait == VanillaTraitsOverhaul.Mountain)
            {
                agent.scale = TraitVisuals.GiantSize(commander, level);
                agent.maxHealth *= 1.5f;
                agent.health = agent.maxHealth;
                if (agent.brain is Swordsman) VanillaTraitsOverhaul.ConvertToTank(agent, commander);
            }
            if (trait == VanillaTraitsOverhaul.Fearless && agent.brain is Swordsman)
            {
                agent.maxHealth *= Mathf.Pow(1.5f, level);
                agent.health = agent.maxHealth;
                if (!agent.GetComponent<JumpComponent>()) ((IAgentSetup)agent.gameObject.AddComponent<JumpComponent>()).Setup(agent);
                if (!agent.GetComponent<JumpAttack>()) ((IAgentSetup)agent.gameObject.AddComponent<JumpAttack>()).Setup(agent);
                VanillaTraitsOverhaul.ConvertToDualWielder(agent, commander);
                TraitVisuals.HideShield(agent);
            }
            var armor = agent.GetComponent<Armor>();
            if (armor && armor.armor != null && armor.armor.Length > 0)
            {
                var values = new float[4];
                for (int i = 0; i < 4; i++)
                {
                    float value = i < armor.armor.Length ? armor.armor[i] : armor.armor[armor.armor.Length - 1];
                    if (trait == VanillaTraitsOverhaul.Mountain) value = (commander ? GiantHeroArmor : GiantArmor)[i];
                    if (trait == VanillaTraitsOverhaul.Ironskin) value *= 4f;
                    if (trait == VanillaTraitsOverhaul.HeavyLoad) value *= 1.25f;
                    values[i] = value;
                }
                armor.armor = values;
            }
            float speed = trait == VanillaTraitsOverhaul.FleetOfFoot ? 1.57f : trait == VanillaTraitsOverhaul.HeavyLoad ? 0.75f
                : trait == VanillaTraitsOverhaul.Ironskin ? 0.6f : trait == VanillaTraitsOverhaul.HeavyWeapons ? 0.8f
                : trait == VanillaTraitsOverhaul.Energetic ? 1.2f : 1f;
            agent.maxSpeed *= speed;
            agent.speed = agent.maxSpeed;
            var stun = agent.GetComponent<Stun>();
            if (stun)
            {
                float factor = 1f;
                if (trait == VanillaTraitsOverhaul.Mountain) factor *= (commander ? GiantHeroStun : GiantStun)[level];
                if (trait == VanillaTraitsOverhaul.SureFooted) factor = 0f;
                if (trait == VanillaTraitsOverhaul.Ironskin) factor *= 0.5f;
                stun.stunMultiplier *= factor;
            }
        }
    }

    [HarmonyPatch(typeof(Shield), nameof(Shield.Setup))]
    internal static class VtoShieldSetupPatch
    {
        // Visible on watchers' copies too: Fearless infantry carry no shield.
        private static void Postfix(Shield __instance)
        {
            if (__instance.agent.isEnglish && TraitRules.Has(__instance.agent.squad, VanillaTraitsOverhaul.Fearless))
                TraitVisuals.HideShield(__instance.agent);
        }
    }

    [HarmonyPatch(typeof(Shield), nameof(Shield.ModifyAttack))]
    internal static class VtoShieldModifyAttackPatch
    {
        private static bool Prefix(Shield __instance) => !VanillaTraitsOverhaul.FearlessDefender(__instance.agent);
    }

    [HarmonyPatch(typeof(Shield), "ShieldCheck")]
    internal static class VtoShieldCheckPatch
    {
        private static readonly FieldInfo NoShield = AccessTools.Field(typeof(Shield), "noShield");

        private static bool Prefix(Shield __instance)
        {
            if (!VanillaTraitsOverhaul.FearlessDefender(__instance.agent)) return true;
            (NoShield?.GetValue(__instance) as AgentState)?.SetActive(true);
            return false;
        }
    }

    [HarmonyPatch(typeof(Shield), "MaybeParry")]
    internal static class VtoShieldParryPatch
    {
        private static bool Prefix(Shield __instance) => !VanillaTraitsOverhaul.FearlessDefender(__instance.agent);
    }

    /// <summary>
    /// Replenish times, ability cooldowns and item charges for the squad's trait. Also on watchers, whose ability buttons
    /// (NativeDefenderControls) have to show the same cooldowns as the leader's.
    /// </summary>
    [HarmonyPatch(typeof(EnglishSquad), nameof(EnglishSquad.OnSquadSpawnComplete))]
    internal static class VtoSquadSpawnPatch
    {
        private static readonly FieldInfo ChargesPerIsland = AccessTools.Field(typeof(ActiveAbility), "_chargesPerIsland");
        private static readonly FieldInfo ChargesRemaining = AccessTools.Field(typeof(ActiveAbility), "_chargesRemaining");

        private static void Postfix(EnglishSquad __instance)
        {
            if (__instance.hero == null) return;
            string trait = TraitRules.TraitOf(__instance);
            if (trait == null) return;
            var replenish = __instance.upgradeManager.GetUpgrade<ReplenishAbility>();
            if (replenish)
            {
                if (trait == VanillaTraitsOverhaul.Popular) replenish.replenishTime *= 0.85f;
                if (trait == VanillaTraitsOverhaul.RousingSpeeches) replenish.replenishTime *= 0.68f;
                // Here too, not only on the leader: a teammate's button then shows the ban instead of a click the leader refuses.
                if (trait == VanillaTraitsOverhaul.Mountain) replenish.BanAbility("Giants cannot replenish", null, null);
            }
            foreach (SerializableHeroUpgrade upgrade in __instance.hero.upgrades)
            {
                if (upgrade == null || !upgrade.definition || !upgrade.definition.AbilityPrefab) continue;
                var prefabAbility = upgrade.definition.AbilityPrefab.GetComponent<ActiveAbility>();
                if (!prefabAbility) continue;
                foreach (UpgradeComponent component in __instance.upgradeManager)
                {
                    var ability = component as ActiveAbility;
                    if (!ability || ability.GetType() != prefabAbility.GetType()) continue;
                    if (trait == VanillaTraitsOverhaul.Energetic) ability.modifyCooldown(0.5f);
                    if (upgrade.definition.typeEnum == HeroUpgradeTypeEnum.Skill && trait == VanillaTraitsOverhaul.Skillful) ability.modifyCooldown(0.67f);
                    if (upgrade.definition.typeEnum == HeroUpgradeTypeEnum.Item && trait == VanillaTraitsOverhaul.Collector) ability.modifyCooldown(0.67f);
                    if (trait == VanillaTraitsOverhaul.HeavyLoad && ability.hasLimitedCharges && ChargesPerIsland != null && ChargesRemaining != null)
                    {
                        ChargesPerIsland.SetValue(ability, (int)ChargesPerIsland.GetValue(ability) + 1);
                        ChargesRemaining.SetValue(ability, (int)ChargesRemaining.GetValue(ability) + 1);
                    }
                }
            }
        }
    }

    /// <summary>Damage, knockback and stun for both the attacker's and the target's traits (Traits Galore's too).</summary>
    [HarmonyPatch(typeof(Agent), nameof(Agent.DealDamage))]
    internal static class TraitDamagePatch
    {
        private static void Prefix(Agent __instance, ref Attack attack)
        {
            if (!TraitRules.Simulates) return;
            string by = TraitRules.TraitOf(attack.killerSquad);
            if (by != null)
            {
                if (by == VanillaTraitsOverhaul.SureFooted) attack.knockback *= 1.25f;
                if (by == VanillaTraitsOverhaul.Sharp) { attack.damage *= 2f; attack.knockback *= 0.75f; attack.stun *= 0.75f; }
                if (by == VanillaTraitsOverhaul.HeavyWeapons) { attack.damage *= 1.1f; attack.knockback *= 1.75f; attack.stun *= 1.6f; }
                if (by == VanillaTraitsOverhaul.Mountain) { attack.damage *= 1.2f; attack.knockback *= 1.4f; }
                if (by == VanillaTraitsOverhaul.Fearless && attack.monoAttacker is Archery) { attack.damage *= 1.3f; attack.knockback *= 1.3f; }
                attack.damage *= TraitsGalore.DamageDealt(by);
            }
            string target = TraitRules.TraitOf(__instance.squad);
            if (target != null)
            {
                if (target == VanillaTraitsOverhaul.SureFooted) attack.knockback *= 0.2f;
                if (target == VanillaTraitsOverhaul.Ironskin) attack.knockback *= 0.5f;
                attack.damage *= TraitsGalore.DamageTaken(target);
            }
        }

        private static void Postfix(Agent __instance, Attack attack)
        {
            if (TraitRules.Simulates && TraitRules.Has(attack.killerSquad, TraitsGalore.Sick) && __instance.aliveState.active)
                SickDot.Infect(__instance);
        }
    }

    [HarmonyPatch(typeof(Agent), "MaybeFixNavPos")]
    internal static class VtoArcherWalkPatch
    {
        // Fearless archers keep walking while they aim and shoot.
        private static void Postfix(Agent __instance)
        {
            if (!VanillaTraitsOverhaul.FearlessDefender(__instance)) return;
            if (__instance.brain is Archery archery && (archery.aiming.active || archery.shooting.active || (archery.ready.active && __instance.enemyAgent)))
            {
                archery.order.ApplyWalk();
                __instance.movability = 1f;
            }
        }
    }

    [HarmonyPatch(typeof(HeroTraitExtraUnit), nameof(HeroTraitExtraUnit.OnAppliedToSquad))]
    internal static class VtoPopularSquadPatch
    {
        private static void Postfix(HeroTraitExtraUnit __instance, EnglishSquad squad)
        {
            if (__instance.name == VanillaTraitsOverhaul.Popular) squad.maxCount += 2;
        }
    }

    [HarmonyPatch]
    internal static class VtoPopularRankPatch
    {
        private static MethodBase TargetMethod()
        {
            Type rankModifier = AccessTools.TypeByName("Voxels.TowerDefense.HeroGeneration.MonoHero+IRankModifier");
            return rankModifier != null ? typeof(HeroTraitExtraUnit).GetInterfaceMap(rankModifier).TargetMethods[0] : null;
        }

        private static void Postfix(HeroUpgradeDefinition __instance, ref int rankCount)
        {
            if (__instance.name == VanillaTraitsOverhaul.Popular) rankCount += 2;
        }
    }

    [HarmonyPatch(typeof(Swordsman), "PursuitUpdate")]
    internal static class VtoRelentlessPursuitPatch
    {
        private static void Prefix(Swordsman __instance) => Relentless(__instance);

        // Mountain and Fearless commanders don't break off to hold their tile.
        public static void Relentless(Swordsman swordsman)
        {
            if (!TraitRules.Simulates) return;
            var squad = swordsman.agent.squad as EnglishSquad;
            if (squad && squad.heroAgent == swordsman.agent &&
                (TraitRules.Has(squad, VanillaTraitsOverhaul.Mountain) || TraitRules.Has(squad, VanillaTraitsOverhaul.Fearless)))
                swordsman.agent.orderDist = 0f;
        }
    }

    [HarmonyPatch(typeof(Swordsman), "ReadyUpdate")]
    internal static class VtoRelentlessReadyPatch
    {
        private static void Prefix(Swordsman __instance) => VtoRelentlessPursuitPatch.Relentless(__instance);
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.Intimidate))]
    internal static class VtoFearlessIntimidatePatch
    {
        private static bool Prefix(Agent __instance) => !(TraitRules.Simulates && __instance.isEnglish &&
            (TraitRules.Has(__instance.squad, VanillaTraitsOverhaul.Fearless) || TraitRules.Has(__instance.squad, VanillaTraitsOverhaul.Mountain)));
    }

    // Leap attacks given to infantry (Fearless) must not jump to nowhere.
    [HarmonyPatch]
    internal static class VtoJumpGuardPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(JumpComponent), "PerformJump", new[] { typeof(NavPos), typeof(IProjectileSolver) });

        private static bool Prefix(NavPos targetPos) => targetPos.transform && targetPos.valid;
    }

    [HarmonyPatch]
    internal static class VtoJumpGuardRefPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(JumpComponent), "PerformJump", new[] { typeof(NavPos).MakeByRefType(), typeof(Vector3), typeof(IProjectileSolver) });

        private static bool Prefix(ref NavPos targetPos) => targetPos.transform && targetPos.valid;
    }

    [HarmonyPatch(typeof(JumpAttack), "PlungeJump")]
    internal static class VtoPlungeGuardPatch
    {
        private static readonly FieldInfo PrepareState = AccessTools.Field(typeof(JumpAttack), "plungePrepareState");

        private static bool Prefix(JumpAttack __instance) => PrepareState?.GetValue(__instance) is AgentState state && state.active;
    }

    [HarmonyPatch(typeof(PlungeAttackComponent), "PlungeJump")]
    internal static class VtoPlungeComponentGuardPatch
    {
        private static readonly FieldInfo Solver = AccessTools.Field(typeof(PlungeAttackComponent), "jumpSolver");

        private static bool Prefix(PlungeAttackComponent __instance) => Solver?.GetValue(__instance) != null;
    }

    /// <summary>Fearless infantry leap a moment apart, and not straight after landing (defenders only: Vikings jump as before).</summary>
    [HarmonyPatch(typeof(JumpAttack), "CanJumpAttack")]
    internal static class VtoJumpRhythmPatch
    {
        private static readonly Dictionary<int, float> delays = new Dictionary<int, float>();
        private static readonly FieldInfo PlungeState = AccessTools.Field(typeof(JumpAttack), "plungeState");

        private static void Postfix(JumpAttack __instance, ref bool __result)
        {
            if (!TraitRules.Simulates || !__instance.agent || !__instance.agent.isEnglish) return;
            int id = __instance.GetInstanceID();
            if (!__result) { delays.Remove(id); return; }
            if (PlungeState?.GetValue(__instance) is AgentState plunge && plunge.timeSinceDeactivation < 2.5f) { __result = false; return; }
            if (!delays.TryGetValue(id, out float at)) { delays[id] = Time.time + Random.Range(0.05f, 0.3f); __result = false; }
            else if (Time.time < at) __result = false;
            else delays.Remove(id);
        }
    }

    [HarmonyPatch(typeof(Spear), "MaybeBreak")]
    internal static class VtoSpearHoldPatch
    {
        // Fearless pikemen don't break formation.
        private static bool Prefix(Spear __instance, ref bool __result)
        {
            if (!TraitRules.Simulates || !TraitRules.Has(__instance.agent.squad, VanillaTraitsOverhaul.Fearless)) return true;
            __result = false;
            return false;
        }
    }

    /// <summary>Upgrade prices for Skillful and Collector (on every screen, so everyone sees the price the leader charges).</summary>
    [HarmonyPatch]
    internal static class VtoUpgradeCostPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(HeroDefinition), "GetUpgradeCost", new[] { typeof(HeroUpgradeDefinition), typeof(int), typeof(bool).MakeByRefType() });

        private static void Postfix(HeroDefinition __instance, HeroUpgradeDefinition def, ref int __result, ref bool discounted)
        {
            if ((TraitRules.Has(__instance, VanillaTraitsOverhaul.Skillful) && def.typeEnum == HeroUpgradeTypeEnum.Skill) ||
                (TraitRules.Has(__instance, VanillaTraitsOverhaul.Collector) && def.typeEnum == HeroUpgradeTypeEnum.Item))
            {
                __result = Mathf.Max(0, Mathf.CeilToInt(__result * 0.5f));
                discounted = true;
            }
        }
    }

    [HarmonyPatch(typeof(Swordsman), nameof(Swordsman.UpdateStamina))]
    internal static class VtoStaminaRegenPatch
    {
        private static readonly FieldInfo Stamina = AccessTools.Field(typeof(Swordsman), "stamina");

        private static void Prefix(Swordsman __instance)
        {
            if (!TraitRules.Simulates || Stamina == null || !TraitRules.Has(__instance.agent.squad, VanillaTraitsOverhaul.FleetOfFoot)) return;
            Stamina.SetValue(__instance, Mathf.MoveTowards((float)Stamina.GetValue(__instance), 1f, Time.deltaTime * 0.2f));
        }
    }

    // ---- Attack timing for the squad's trait (Fleet of Foot, Ironskin, Axe Squad, Light Weapons) -------------------

    internal static class TraitTiming
    {
        /// <summary>Time between attacks (×): below 1 attacks faster.</summary>
        public static float AttackTime(Squad squad)
        {
            string trait = TraitRules.TraitOf(squad);
            if (trait == null) return 1f;
            float time = TraitsGalore.AttackTime(trait);
            if (trait == VanillaTraitsOverhaul.FleetOfFoot) time *= 0.8f;
            if (trait == VanillaTraitsOverhaul.Ironskin) time *= 1.43f;
            return time;
        }
    }

    [HarmonyPatch(typeof(Swordsman), nameof(Swordsman.Setup))]
    internal static class TraitSwordsmanTimingPatch
    {
        private static readonly FieldInfo StaminaCost = AccessTools.Field(typeof(Swordsman), "attackStaminaCost");

        private static void Postfix(Swordsman __instance)
        {
            if (!TraitRules.Simulates || StaminaCost == null) return;
            float cost = TraitRules.Has(__instance.agent.squad, VanillaTraitsOverhaul.FleetOfFoot) ? 0.5f : 1f;
            cost *= TraitsGalore.AttackTime(TraitRules.TraitOf(__instance.agent.squad));
            if (cost != 1f) StaminaCost.SetValue(__instance, (float)StaminaCost.GetValue(__instance) * cost);
        }
    }

    [HarmonyPatch(typeof(Archery), nameof(Archery.Setup))]
    internal static class TraitArcheryTimingPatch
    {
        private static void Postfix(Archery __instance)
        {
            if (!TraitRules.Simulates) return;
            float time = TraitTiming.AttackTime(__instance.agent.squad);
            if (time == 1f || __instance._archerySettings == null) return;
            var settings = (Archery.ArcherySettings[])__instance._archerySettings.Clone();
            for (int i = 0; i < settings.Length; i++) { settings[i].cooldown *= time; settings[i].holdTime *= time; }
            __instance._archerySettings = settings;
        }
    }

    [HarmonyPatch(typeof(Spear), nameof(Spear.Setup))]
    internal static class TraitSpearTimingPatch
    {
        private static readonly FieldInfo Recharge = AccessTools.Field(typeof(Spear), "rechargeTime");

        private static void Postfix(Spear __instance)
        {
            if (!TraitRules.Simulates) return;
            if (TraitRules.Has(__instance.agent.squad, VanillaTraitsOverhaul.Fearless)) __instance.spearLength *= 1.3f;
            float time = TraitTiming.AttackTime(__instance.agent.squad);
            if (time == 1f || Recharge == null || !(Recharge.GetValue(__instance) is float[] times)) return;
            var scaled = new float[times.Length];
            for (int i = 0; i < times.Length; i++) scaled[i] = times[i] * time;
            Recharge.SetValue(__instance, scaled);
        }
    }

    // ---- Longer trait text in the game's info panels -------------------------------------------------------------

    internal class LayoutFixer : MonoBehaviour
    {
        private int framesToWait;

        public void TriggerRebuild() => framesToWait = 5;
        private void OnEnable() => TriggerRebuild();

        private void LateUpdate()
        {
            if (framesToWait <= 0 || --framesToWait > 0) return;
            if (transform is RectTransform rect) UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(rect);
        }

        public static void Rebuild(Component panel) =>
            (panel.GetComponent<LayoutFixer>() ?? panel.gameObject.AddComponent<LayoutFixer>()).TriggerRebuild();
    }

    [HarmonyPatch]
    internal static class VtoInfoPanelLayoutPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("Voxels.TowerDefense.UI.MetaInventory.InfoPanel"), "Setup");
        private static void Postfix(Component __instance) => LayoutFixer.Rebuild(__instance);
    }

    [HarmonyPatch]
    internal static class VtoLevelInfoLayoutPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("Voxels.TowerDefense.UI.MetaInventory.UpgradeLevelInfo"), "Setup");
        private static void Postfix(Component __instance) => LayoutFixer.Rebuild(__instance);
    }

    [HarmonyPatch(typeof(UpgradeInfo), nameof(UpgradeInfo.Setup))]
    internal static class VtoUpgradeInfoLayoutPatch
    {
        private static void Postfix(UpgradeInfo __instance) => LayoutFixer.Rebuild(__instance);
    }
}
