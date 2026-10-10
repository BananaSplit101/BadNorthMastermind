using System;
using System.Collections.Generic;
using BadNorthMP.Spectator;
using HarmonyLib;
using I2.Loc;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Traits
{
    /// <summary>
    /// Commander traits the mod adds (GMW's Traits Galore, ported with permission). They join the game's own list of
    /// upgrades (ResourceList), which is how saves find a trait by name, how rescued commanders roll random traits and how
    /// every player's game knows a shared campaign's traits. Their text joins the game's localization; their icons are
    /// embedded. They never go into the player's profile, so removing the mod can't break it.
    /// </summary>
    internal static class CustomTraits
    {
        public static readonly List<HeroUpgradeDefinition> All = new List<HeroUpgradeDefinition>();
        private static readonly Dictionary<string, string> terms = new Dictionary<string, string>();
        private static HeroUpgradeType traitType;

        public static bool IsCustom(HeroUpgradeDefinition definition) => definition && All.Contains(definition);

        /// <summary>A new trait of type T (its behaviour lives in the class): its id is what saves store.</summary>
        public static T Register<T>(string id, string name, string shortText, string description, string icon) where T : HeroUpgradeDefinition
        {
            var trait = ScriptableObject.CreateInstance<T>();
            trait.name = id;
            trait.hideFlags = HideFlags.DontUnloadUnusedAsset;
            // uniqueId (what OnEnable copied from the then-empty name) has a private setter.
            var uniqueId = AccessTools.Property(typeof(HeroUpgradeDefinition), "uniqueId");
            if (uniqueId != null && uniqueId.GetSetMethod(true) != null) uniqueId.GetSetMethod(true).Invoke(trait, new object[] { id });
            else AccessTools.Field(typeof(HeroUpgradeDefinition), "<uniqueId>k__BackingField")?.SetValue(trait, id);
            trait.upgradeType = TraitType();
            trait.affectsPortrait = false;
            string key = "BNMP/TRAIT/" + id.ToUpperInvariant();
            trait.nameTerm = key + "/NAME";
            trait.shortDescription = key + "/SHORT";
            trait.levels = new[] { new HeroUpgradeDefinition.Level { cost = 0, description = key + "/DESC", prepurchaseDescription = key + "/DESC" } };
            Term(trait.nameTerm, name);
            Term(trait.shortDescription, shortText);
            Term(key + "/DESC", description);
            Texture2D texture = EmbeddedAssets.Texture(icon);
            if (texture) trait.infoSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            ResourceList<HeroUpgradeDefinition>.dictionary[id] = trait;
            if (!ResourceList<HeroUpgradeDefinition>.list.Contains(trait)) ResourceList<HeroUpgradeDefinition>.list.Add(trait);
            All.Add(trait);
            return trait;
        }

        /// <summary>The game's own trait type (its icon mask), so the new traits look like the others.</summary>
        private static HeroUpgradeType TraitType()
        {
            if (traitType) return traitType;
            foreach (HeroUpgradeDefinition upgrade in ResourceList<HeroUpgradeDefinition>.list)
                if (upgrade && upgrade.upgradeType && upgrade.upgradeType.typeEnum == HeroUpgradeTypeEnum.Trait && !IsCustom(upgrade))
                { traitType = upgrade.upgradeType; return traitType; }
            traitType = ScriptableObject.CreateInstance<HeroUpgradeType>();
            traitType.typeEnum = HeroUpgradeTypeEnum.Trait;
            traitType.canBeStartItem = true;
            traitType.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return traitType;
        }

        /// <summary>Text for a term, in every language source loaded now or later.</summary>
        public static void Term(string term, string text)
        {
            terms[term] = text;
            if (LocalizationManager.Sources == null) return;
            foreach (LanguageSourceData source in LocalizationManager.Sources) AddTerm(source, term, text, true);
        }

        public static void AddTerms(LanguageSourceData source)
        {
            foreach (var term in terms) AddTerm(source, term.Key, term.Value, false);
            source.UpdateDictionary(false);
        }

        private static void AddTerm(LanguageSourceData source, string term, string text, bool update)
        {
            if (source == null) return;
            TermData data = source.GetTermData(term, false);
            if (data == null) { source.AddTerm(term); data = source.GetTermData(term, false); }
            data.SetTranslation(0, text, null);
            if (update) source.UpdateDictionary(false);
        }
    }

    [HarmonyPatch(typeof(LocalizationManager), "AddSource")]
    internal static class CustomTraitTermsPatch
    {
        private static void Postfix(LanguageSourceData Source) => CustomTraits.AddTerms(Source);
    }

    /// <summary>The mod's traits stay out of the player's profile (a meta-reward unlock would save one there).</summary>
    [HarmonyPatch]
    internal static class CustomTraitProfilePatch
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("Voxels.TowerDefense.ProfileInternals.MetaInventory");
            foreach (var method in AccessTools.GetDeclaredMethods(type))
                if (method.Name == "Add" && method.GetParameters().Length > 0 && method.GetParameters()[0].ParameterType == typeof(HeroUpgradeDefinition))
                    return method;
            return null;
        }

        private static bool Prefix(HeroUpgradeDefinition __0) => !CustomTraits.IsCustom(__0);
    }

    /// <summary>Who has which trait, and where trait effects run.</summary>
    internal static class TraitRules
    {
        /// <summary>This game simulates its battles (single player, or the campaign leader); watchers only mirror them.</summary>
        public static bool Simulates => !SpectatorSession.Active;

        public static string TraitOf(Squad squad)
        {
            var english = squad as EnglishSquad;
            return english && english.hero != null ? TraitOf(english.hero) : null;
        }

        public static string TraitOf(HeroDefinition hero) =>
            hero != null && hero.traitUpgrade != null && hero.traitUpgrade.definition ? hero.traitUpgrade.definition.name : null;

        public static bool Has(Squad squad, string id) => TraitOf(squad) == id;
        public static bool Has(HeroDefinition hero, string id) => TraitOf(hero) == id;
    }
}
