using System;
using System.Collections.Generic;
using BadNorthMP.Spectator;
using BadNorthMP.UI;
using HarmonyLib;
using Steamworks;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI.UpgradeScreen;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Campaign-map upgrades in a party: each player upgrades only their own commanders, from the shared coin bank and
    /// inventory. Other defenders' purchases run natively on their copy for instant feedback and are sent to the leader,
    /// who checks and applies them, then broadcasts each so everyone updates in place without a campaign reload.
    /// </summary>
    internal static class PartyUpgrades
    {
        // When the upgrade menu last closed (see CampaignMapOpenedPatch).
        public static float MenuClosedAt = -10f;

        public static void Init()
        {
            Router.On(MsgType.UpgradeRequest, Receive);
            Router.On(MsgType.UpgradeResult, (sender, r) =>
            {
                if (sender != Roles.DefenderId) return;
                bool ok = r.ReadBoolean();
                string message = r.ReadString();
                ModLog.Info("Upgrade " + (ok ? "applied by the leader: " : "refused by the leader: ") + message);
                if (!ok) GameNotify.Post(message);
            });
            Router.On(MsgType.UpgradeApplied, Applied);
        }

        /// <summary>In a party, a player may only spend on the commanders they own; the mastermind on none.</summary>
        public static bool MayUpgrade(HeroDefinition hero) =>
            !PartyFlow.Deployed || (!Roles.IsMastermind && hero != null && PartyFlow.Owns(SteamSession.Instance.Me, hero));

        /// <summary>Remote defenders use the native menu on their copy of the leader's campaign.</summary>
        public static bool RemoteMenu => SpectatorSession.Active && Roles.IsRemoteDefender && PartyFlow.Deployed && SpectatorSession.ViewingCampaign;

        public static HeroDefinition Focused(SuperUpgradeMenu menu)
        {
            var portrait = Traverse.Create(menu).Field<PortraitToken>("_focusedPortrait").Value;
            return portrait ? portrait.heroDef : null;
        }

        public static string OwnerName(HeroDefinition hero)
        {
            int index = Profile.campaign != null ? Profile.campaign.heroes.IndexOf(hero) : -1;
            var seat = PartyFlow.State.OwnerOf(index);
            return seat != null ? SteamSession.NameOf(new CSteamID(seat.player)) : "nobody";
        }

        /// <summary>Native rule (UpgradeToken.isItem): level-0 items and consumables come out of the shared inventory.</summary>
        private static bool FromInventory(HeroUpgradeDefinition def, int level) =>
            (def.typeEnum == HeroUpgradeTypeEnum.Item && level == 0) || def.typeEnum == HeroUpgradeTypeEnum.Consumable;

        private static bool InInventory(HeroUpgradeDefinition def)
        {
            foreach (SerializableHeroUpgrade item in Profile.campaign.inventory)
                if (item.definition == def) return true;
            return false;
        }

        private static void RemoveFromInventory(HeroUpgradeDefinition def)
        {
            List<SerializableHeroUpgrade> inventory = Profile.campaign.inventory;
            for (int i = 0; i < inventory.Count; i++)
                if (inventory[i].definition == def) { inventory.RemoveAt(i); return; }
        }

        // ---- Native menu purchases ----------------------------------------------------------------

        internal sealed class Purchase
        {
            public HeroDefinition hero;
            public HeroUpgradeDefinition def;
            public int level, coins, inventory, heroLevel;
        }

        /// <summary>Before the native purchase: null when it isn't a purchase this player may make.</summary>
        public static Purchase Begin(SuperUpgradeMenu menu, UpgradeToken token, out bool allow)
        {
            allow = true;
            // The first click only selects the token.
            if (!PartyFlow.Deployed || !token || Traverse.Create(menu).Property<UpgradeToken>("selectedUpgradeToken").Value != token)
                return null;
            HeroDefinition hero = Focused(menu);
            if (!MayUpgrade(hero))
            {
                allow = false;
                FabricWrapper.PostEvent(FabricID.uiError);
                GameNotify.Post(hero != null ? hero.dbgName + " belongs to " + OwnerName(hero) + ". Only they can upgrade or equip them." : "Choose one of your commanders.");
                return null;
            }
            return new Purchase { hero = hero, def = token.upgradeDef, level = token.level, coins = Profile.campaign.coinBank,
                inventory = Profile.campaign.inventory.Count, heroLevel = hero.GetUpgradeLevel(token.upgradeDef) };
        }

        /// <summary>After the native purchase: send it to the leader, or (on the leader) tell everyone.</summary>
        public static void Complete(Purchase purchase)
        {
            if (purchase == null) return;
            var campaign = Profile.campaign;
            bool bought = campaign.coinBank != purchase.coins || campaign.inventory.Count != purchase.inventory ||
                purchase.hero.GetUpgradeLevel(purchase.def) != purchase.heroLevel;
            if (!bought) return;
            int hero = campaign.heroes.IndexOf(purchase.hero);
            if (Roles.IsDefender)
            {
                ModLog.Info("Upgrade: " + purchase.hero.dbgName + " " + purchase.def.name + " level " + purchase.level);
                Broadcast(SteamSession.Instance.Me, hero, purchase.def, purchase.level);
            }
            else if (RemoteMenu)
            {
                SteamSession.Instance.Send(Roles.DefenderId, Packet.Build(MsgType.UpgradeRequest, w =>
                {
                    w.Write(PartyFlow.State.token); w.Write(campaign.seed);
                    w.Write(hero); w.Write(purchase.def.name); w.Write(purchase.level);
                }));
                ModLog.Info("Upgrade sent to the leader: " + purchase.hero.dbgName + " " + purchase.def.name + " level " + purchase.level);
            }
        }

        // ---- Leader ---------------------------------------------------------------------------------

        private static void Receive(CSteamID sender, System.IO.BinaryReader r)
        {
            var session = SteamSession.Instance;
            if (!Roles.IsDefender || sender == Roles.MastermindId || sender == session.Me || !session.Members().Contains(sender)) return;
            int token = r.ReadInt32(), seed = r.ReadInt32(), heroIndex = r.ReadInt32();
            string name = r.ReadString();
            int level = r.ReadInt32();
            bool ok = false;
            string message;
            HeroUpgradeDefinition def = name.Length <= 200 ? ResourceList<HeroUpgradeDefinition>.Get(name) : null;
            if (!PartyFlow.Deployed || token != PartyFlow.State.token || Profile.campaign == null || seed != Profile.campaign.seed)
                message = "That upgrade was for a different campaign.";
            else if (!IslandVote.LeaderOnMap())
                message = "Upgrades wait until the leader is back on the map.";
            else if (heroIndex < 0 || heroIndex >= Profile.campaign.heroes.Count || !PartyFlow.State.Owns(sender.m_SteamID, heroIndex))
                message = "Only a commander's owner can upgrade or equip them.";
            else if (!def)
                message = "Unknown upgrade.";
            else
                ok = Apply(Profile.campaign.heroes[heroIndex], def, level, out message);
            ModLog.Info("Upgrade from " + SteamSession.NameOf(sender) + ": commander " + heroIndex + " " + name + " level " + level + " -> " + message);
            session.Send(sender, Packet.Build(MsgType.UpgradeResult, w => { w.Write(ok); w.Write(message); }));
            if (!ok)
            {
                // Their copy already shows the purchase; send them the real campaign again.
                CampaignSync.Resend(sender);
                return;
            }
            Broadcast(sender, heroIndex, def, level);
            CampaignSync.QuietSave = true;
            try { Profile.SaveCampaign(); }
            finally { CampaignSync.QuietSave = false; }
            RefreshMenu();
        }

        /// <summary>The native purchase (SuperUpgradeMenu.OnClicked) without the menu.</summary>
        private static bool Apply(HeroDefinition hero, HeroUpgradeDefinition def, int level, out string message)
        {
            if (Profile.campaign.gameOver) { message = "The campaign is over."; return false; }
            if (!def.AvailableFor(hero, level)) { message = "That upgrade isn't available for " + hero.dbgName + " any more."; return false; }
            bool fromInventory = FromInventory(def, level);
            if (fromInventory && !InInventory(def)) { message = "That item is no longer in the inventory."; return false; }
            int coins = Profile.campaign.coinBank;
            if (!hero.TryPurchase(def, level, ref coins)) { message = "Not enough coins."; return false; }
            Profile.campaign.coinBank = coins;
            if (fromInventory) RemoveFromInventory(def);
            message = hero.dbgName + " upgraded.";
            return true;
        }

        private static void Broadcast(CSteamID purchaser, int hero, HeroUpgradeDefinition def, int level)
        {
            int coins = Profile.campaign.coinBank;
            SteamSession.Instance.Broadcast(Packet.Build(MsgType.UpgradeApplied, w =>
            {
                w.Write(PartyFlow.State.token); w.Write(Profile.campaign.seed); w.Write(purchaser.m_SteamID);
                w.Write(hero); w.Write(def.name); w.Write(level); w.Write(coins);
            }));
        }

        // ---- Everyone watching the leader's campaign ------------------------------------------------

        private static void Applied(CSteamID sender, System.IO.BinaryReader r)
        {
            if (sender != Roles.DefenderId || !SpectatorSession.Active || Profile.campaign == null) return;
            int token = r.ReadInt32(), seed = r.ReadInt32();
            ulong purchaser = r.ReadUInt64();
            int heroIndex = r.ReadInt32();
            string name = r.ReadString();
            int level = r.ReadInt32(), coins = r.ReadInt32();
            if (token != PartyFlow.State.token || seed != Profile.campaign.seed || heroIndex < 0 || heroIndex >= Profile.campaign.heroes.Count) return;
            HeroUpgradeDefinition def = ResourceList<HeroUpgradeDefinition>.Get(name);
            HeroDefinition hero = Profile.campaign.heroes[heroIndex];
            // The purchaser's own copy already ran the native purchase.
            if (def && purchaser != SteamSession.Instance.Me.m_SteamID)
            {
                if (def.AvailableFor(hero, level))
                {
                    int unlimited = int.MaxValue / 2;
                    hero.TryPurchase(def, level, ref unlimited);
                    if (FromInventory(def, level)) RemoveFromInventory(def);
                }
                else ModLog.Problem("upgrade_desync", "Upgrade from the leader didn't apply here: " + hero.dbgName + " " + name + " level " + level);
            }
            Profile.campaign.coinBank = coins;
            RefreshMenu();
        }

        private static void RefreshMenu()
        {
            var menu = SuperUpgradeMenu.instance;
            // While a newer map is still loading, the menu belongs to the old one: it's rebuilt with the new map.
            if (!menu || (SpectatorSession.Active && !SpectatorSession.ViewingCampaign)) return;
            try
            {
                menu.UpdateCoinBank();
                if (!menu.isOpen) return;
                menu.RefreshPortraits();
                Traverse.Create(menu).Method("UpdateFocusedHero").GetValue();
            }
            catch (Exception e) { ModLog.Warn("Upgrade menu refresh failed: " + e.Message); }
        }
    }
}
