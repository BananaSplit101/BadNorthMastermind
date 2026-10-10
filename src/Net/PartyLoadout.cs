using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Net
{
    internal static class PartyLoadout
    {
        public static string[] Catalog(bool trait)
        {
            var names = new List<string>();
            if (Profile.userSave == null || Profile.userSave.inventory == null) return names.ToArray();
            var inventory = Profile.userSave.inventory;
            var source = trait ? inventory.startingTraits : inventory.startingItems;
            if (source == null) return names.ToArray();
            foreach (var upgrade in source)
                if (upgrade && Valid(upgrade, trait) && !names.Contains(upgrade.name)) names.Add(upgrade.name);
            names.Sort(StringComparer.Ordinal); return names.ToArray();
        }
        private static bool Valid(HeroUpgradeDefinition upgrade, bool trait) => upgrade && upgrade.upgradeType && (trait ? upgrade.typeEnum == HeroUpgradeTypeEnum.Trait :
            upgrade.typeEnum == HeroUpgradeTypeEnum.Item || upgrade.typeEnum == HeroUpgradeTypeEnum.Consumable);
        public static HeroUpgradeDefinition Resolve(string name, bool trait)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var upgrade in Resources.FindObjectsOfTypeAll<HeroUpgradeDefinition>())
                if (upgrade.name == name && Valid(upgrade, trait)) return upgrade;
            throw new IOException("Starting upgrade is missing: " + name);
        }
        public static string Label(string name, bool trait)
        {
            var upgrade = Resolve(name, trait);
            return upgrade ? upgrade.dbgName : trait ? "No Trait" : "No Item";
        }
        public static void PreparePreview(HeroDefinition hero, string trait, string item)
        {
            // Native SetupHeroState fires OnPurchased, including resurrection effects. Previews only carry definitions.
            hero.recruited = true;
            var traitDef = Resolve(trait, true); var itemDef = Resolve(item, false);
            hero.traitUpgrade = traitDef ? new SerializableHeroUpgrade(traitDef) : null;
            hero.itemUpgrade = itemDef ? new SerializableHeroUpgrade(itemDef) : null;
            hero.upgrades.Clear();
            if (hero.traitUpgrade != null) hero.upgrades.Add(hero.traitUpgrade);
            if (hero.itemUpgrade != null) hero.upgrades.Add(hero.itemUpgrade);
        }
        public static void IsolatePortraitAtlas(UnityEngine.Component mono)
        {
            // The native new-game atlas is sized for two cards. Each temporary card owns and releases its own sheet.
            var atlas = mono.gameObject.AddComponent<SpriteComposing.SpriteComposer>();
            atlas.MaybeInitalize();
        }
        public static void Commit(HeroDefinition hero)
        {
            TransferConsumable(hero);
            var trait = hero.traitUpgrade == null ? null : hero.traitUpgrade.definition;
            var item = hero.itemUpgrade == null ? null : hero.itemUpgrade.definition;
            hero.SetupHeroState(true, trait, (HeroUpgradeDefinition)null, item);
        }
        public static void TransferConsumable(HeroDefinition hero)
        {
            if (hero.itemUpgrade != null && hero.itemUpgrade.definition.typeEnum == HeroUpgradeTypeEnum.Consumable)
            {
                Profile.campaign.inventory.Add(hero.itemUpgrade.definition);
                hero.upgrades.Remove(hero.itemUpgrade);
                hero.itemUpgrade = null;
            }
        }
    }
}
