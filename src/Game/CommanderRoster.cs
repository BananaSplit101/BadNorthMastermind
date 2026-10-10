using System;
using System.Collections.Generic;
using System.IO;

namespace BadNorthMP.Game
{
    /// <summary>A persistent Viking squad the mastermind owns. Type is a VikingAgent.Type.</summary>
    internal sealed class VikingCommander
    {
        public int id;
        public int type;
        /// <summary>Islands to wait before deploying again: deploying sets 2, every island start counts down,
        /// so a commander sits out exactly the next island.</summary>
        public int rest;
        /// <summary>The squad's trait and flag (Game.VikingTrait), rolled when recruited; 0 = none (older saves).</summary>
        public int trait;
        /// <summary>The war chest gold it was recruited for (selling gives back a share); 0 = unknown (older saves).</summary>
        public int price;
        public VikingCommander Clone() => new VikingCommander { id = id, type = type, rest = rest, trait = trait, price = price };
    }

    internal enum CommanderStatus : byte { Ready, Resting, Deployed }

    /// <summary>The commander rules, free of game types so they can be tested on their own.</summary>
    internal static class CommanderRoster
    {
        public static bool ValidType(int type) => type >= 0 && type < EconomyConfig.CommanderSquadSize.Length;

        public static int SquadSize(int type) => ValidType(type) ? EconomyConfig.CommanderSquadSize[type] : 0;

        public static int Price(int unitCost, int type) =>
            unitCost <= 0 || !ValidType(type) ? 0 : EconomyConfig.CommanderCostMultiplier * unitCost * SquadSize(type);

        /// <summary>The price with <paramref name="owned"/> commanders already in the roster: each one makes the next dearer.</summary>
        public static int Price(int unitCost, int type, int owned) =>
            (int)Math.Round(Price(unitCost, type) * (1.0 + EconomyConfig.CommanderPriceStepPerOwned * Math.Max(0, owned)), MidpointRounding.AwayFromZero);

        /// <summary>What selling gives back: a share of what it cost (of its base price, for commanders from older saves).</summary>
        public static int SellValue(VikingCommander commander, int unitCost) =>
            commander == null ? 0 : (int)Math.Floor((commander.price > 0 ? commander.price : Price(unitCost, commander.type)) * (double)EconomyConfig.CommanderSellBack);

        /// <summary>Any commander can be sold except one fighting on the current island.</summary>
        public static bool CanSell(VikingCommander commander, ICollection<int> deployedHere, out string why)
        {
            why = null;
            if (commander == null) why = "that commander is already gone";
            else if (deployedHere != null && deployedHere.Contains(commander.id)) why = "a commander can't be sold while it's on the island";
            return why == null;
        }

        public static CommanderStatus Status(VikingCommander commander, ICollection<int> deployedHere) =>
            deployedHere != null && deployedHere.Contains(commander.id) ? CommanderStatus.Deployed
            : commander.rest > 0 ? CommanderStatus.Resting : CommanderStatus.Ready;

        public static bool CanBuy(List<VikingCommander> roster, int type, int price, float warChest, out string why)
        {
            why = null;
            if (!ValidType(type) || price <= 0) why = "that Viking type can't lead a squad yet";
            else if (roster.Count >= EconomyConfig.MaxCommanders) why = $"you already have {EconomyConfig.MaxCommanders} commanders";
            else if (price > warChest + 0.001f) why = $"a commander costs {price} gold from the war chest";
            return why == null;
        }

        public static bool CanDeploy(VikingCommander commander, ICollection<int> deployedHere, out string why)
        {
            why = null;
            if (commander == null) why = "unknown commander";
            else if (deployedHere.Contains(commander.id)) why = "that commander is already on this island";
            else if (commander.rest > 0) why = "that commander is resting this island";
            else if (deployedHere.Count >= EconomyConfig.MaxCommandersDeployed)
                why = $"only {EconomyConfig.MaxCommandersDeployed} commanders can join one island";
            return why == null;
        }

        public static void MarkDeployed(VikingCommander commander) => commander.rest = 2;

        /// <summary>A new island begins: every resting commander gets one island closer to ready.</summary>
        public static void IslandBegins(List<VikingCommander> roster)
        {
            foreach (VikingCommander commander in roster) commander.rest = Math.Max(0, commander.rest - 1);
        }

        public static List<VikingCommander> Copy(List<VikingCommander> roster)
        {
            var copy = new List<VikingCommander>();
            foreach (VikingCommander commander in roster) copy.Add(commander.Clone());
            return copy;
        }

        public static bool ValidTrait(int trait) => trait >= 0 && trait <= EconomyConfig.VikingTraitCount;
        /// <summary>Save format; with traits since save version 3, with prices since version 4.</summary>
        public static void Write(BinaryWriter w, List<VikingCommander> roster, int nextId, bool traits = true, bool prices = true)
        {
            w.Write(nextId);
            w.Write((byte)roster.Count);
            foreach (VikingCommander commander in roster)
            {
                w.Write(commander.id);
                w.Write((byte)commander.type);
                w.Write((byte)commander.rest);
                if (traits) w.Write((byte)commander.trait);
                if (prices) w.Write(commander.price);
            }
        }

        public static List<VikingCommander> Read(BinaryReader r, out int nextId, bool traits = true, bool prices = true)
        {
            nextId = r.ReadInt32();
            int count = r.ReadByte();
            if (nextId < 1 || count > EconomyConfig.MaxCommanders) throw new IOException("Invalid commander roster");
            var roster = new List<VikingCommander>();
            var ids = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                var commander = new VikingCommander { id = r.ReadInt32(), type = r.ReadByte(), rest = r.ReadByte() };
                if (traits) commander.trait = r.ReadByte();
                if (prices) commander.price = r.ReadInt32();
                if (commander.id < 1 || commander.id >= nextId || !ids.Add(commander.id) || !ValidType(commander.type) ||
                    commander.rest > 2 || !ValidTrait(commander.trait) || commander.price < 0 || commander.price > 1000000)
                    throw new IOException("Invalid commander");
                roster.Add(commander);
            }
            return roster;
        }
    }
}
