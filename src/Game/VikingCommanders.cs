using Voxels.TowerDefense;

namespace BadNorthMP.Game
{
    /// <summary>
    /// Leader side: the mastermind's Viking commanders. Bought from the war chest (CommanderRoster rules), landed on
    /// an island by their own ship, and ordered directly: move to a tile, burn a house, or follow their own plan.
    /// </summary>
    internal static class VikingCommanders
    {
        public const byte OrderMove = 0, OrderBurn = 1, OrderRelease = 2;

        public static string TypeName(int type)
        {
            switch ((VikingAgent.Type)type)
            {
                case VikingAgent.Type.Sword: return "Swordsman";
                case VikingAgent.Type.SwordShield: return "Shieldbearer";
                case VikingAgent.Type.Archer: return "Archer";
                case VikingAgent.Type.Tank: return "Brute";
                case VikingAgent.Type.Beserker: return "Berserker";
                case VikingAgent.Type.AxeThrower: return "Axe thrower";
                case VikingAgent.Type.TankArcher: return "Brute archer";
                case VikingAgent.Type.Twohanded: return "Champion";
                default: return "Viking";
            }
        }

        /// <summary>"6 Swordsmen", "1 Brute".</summary>
        public static string Count(int count, int type)
        {
            string name = TypeName(type);
            if (count == 1) return "1 " + name;
            return count + " " + (name.EndsWith("man") ? name.Substring(0, name.Length - 3) + "men" : name + "s");
        }

        /// <summary>Vikings the commander lands with: its squad size plus free count-upgrade Vikings.</summary>
        public static int Crew(int type) =>
            MastermindUpgrades.CrewCount(CommanderRoster.SquadSize(type), MastermindSession.UpgradeLevel((VikingAgent.Type)type, UnitUpgrade.Count));

        public static bool TryBuy(long session, int nextId, int type, out string message)
        {
            MastermindUpgradeSave.EnsureCampaign();
            if (!MastermindUpgrades.CanBuy())
            { message = "recruiting isn't ready; return to the map or resume the battle"; return false; }
            if (session != MastermindSession.UpgradeSession || nextId != MastermindSession.NextCommanderId)
            { message = "your roster changed; wait for the latest state"; return false; }
            VikingReference reference = MastermindUpgrades.AvailableTypes().Find(v => (int)v.type == type);
            if (!reference)
            { message = "that Viking type isn't unlocked yet"; return false; }
            int price = CommanderRoster.Price(Economy.UnitCost(reference), type, MastermindSession.Commanders.Count);
            if (!CommanderRoster.CanBuy(MastermindSession.Commanders, type, price, MastermindSession.WarChest, out message) ||
                !MastermindSession.TrySpendWarChest(price))
            { message = message ?? "not enough war chest gold"; return false; }
            if (!MastermindSession.AddCommander(type, price))
            {
                MastermindSession.Refund(price);
                message = "couldn't save the commander; gold refunded (see the campaign leader's log)";
                return false;
            }
            message = $"{TypeName(type)} commander recruited ({price} gold)";
            return true;
        }

        /// <summary>Sell a commander back for a share of what it cost (CommanderRoster.SellValue).</summary>
        public static bool TrySell(long session, int id, out string message)
        {
            MastermindUpgradeSave.EnsureCampaign();
            if (!MastermindUpgrades.CanBuy())
            { message = "selling isn't possible right now; return to the map or resume the battle"; return false; }
            if (session != MastermindSession.UpgradeSession)
            { message = "your roster changed; wait for the latest state"; return false; }
            VikingCommander commander = MastermindSession.Commander(id);
            if (!CommanderRoster.CanSell(commander, Economy.Active ? Economy.DeployedCommanders.Keys : null, out message)) return false;
            VikingReference reference = MastermindUpgrades.AvailableTypes().Find(v => (int)v.type == commander.type);
            int refund = CommanderRoster.SellValue(commander, reference ? Economy.UnitCost(reference) : 0);
            if (!MastermindSession.SellCommander(id, refund))
            { message = "couldn't save the sale; nothing changed (see the campaign leader's log)"; return false; }
            message = $"{TypeName(commander.type)} commander sold for {refund} gold";
            return true;
        }
    }
}
