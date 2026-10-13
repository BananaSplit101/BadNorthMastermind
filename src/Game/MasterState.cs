using System.IO;
using Voxels.TowerDefense;

namespace BadNorthMP.Game
{
    /// <summary>Upgrade data appended to Net.MasterState; also validates the saved level layout.</summary>
    internal sealed class MasterUpgradeState
    {
        public long session;
        public int revision;
        public int islandIndex;
        public bool canBuy;
        public float gold;
        public int[] unitCosts = new int[MastermindUpgrades.TypeCount];
        public byte[] levels = new byte[MastermindUpgrades.TypeCount * 3];

        /// <summary>The mastermind's Viking commanders as the leader sees them.</summary>
        public sealed class CommanderInfo { public int id, type, crew, trait, sellValue; public CommanderStatus status; }
        public int nextCommanderId = 1;
        public readonly System.Collections.Generic.List<CommanderInfo> commanders = new System.Collections.Generic.List<CommanderInfo>();

        public int Level(VikingAgent.Type type, UnitUpgrade kind) =>
            MastermindUpgrades.ValidType(type) && MastermindUpgrades.ValidKind(kind)
                ? levels[(int)type * 3 + (int)kind] : 0;

        public void Write(BinaryWriter writer)
        {
            writer.Write(session);
            writer.Write(revision);
            writer.Write(islandIndex);
            writer.Write(canBuy);
            writer.Write(gold);
            writer.Write(levels);
            foreach (int cost in unitCosts) writer.Write(cost);
            writer.Write(nextCommanderId);
            writer.Write((byte)commanders.Count);
            foreach (CommanderInfo commander in commanders)
            {
                writer.Write(commander.id);
                writer.Write((byte)commander.type);
                writer.Write((byte)commander.status);
                writer.Write((byte)commander.crew);
                writer.Write((byte)commander.trait);
                writer.Write(commander.sellValue);
            }
        }

        public static MasterUpgradeState Read(BinaryReader reader)
        {
            var state = new MasterUpgradeState
            {
                session = reader.ReadInt64(), revision = reader.ReadInt32(),
                islandIndex = reader.ReadInt32(), canBuy = reader.ReadBoolean(), gold = reader.ReadSingle(),
            };
            state.levels = reader.ReadBytes(MastermindUpgrades.TypeCount * 3);
            if (state.session <= 0 || state.revision < 0 || float.IsNaN(state.gold) ||
                float.IsInfinity(state.gold) || state.gold < 0 || state.levels.Length != MastermindUpgrades.TypeCount * 3)
                throw new IOException("Invalid upgrade snapshot");
            int purchased = 0;
            foreach (byte level in state.levels)
            {
                if (level > MastermindUpgrades.MaxLevel) throw new IOException("Invalid upgrade level");
                purchased += level;
            }
            if (purchased != state.revision) throw new IOException("Invalid upgrade revision");
            for (int i = 0; i < state.unitCosts.Length; i++)
            {
                state.unitCosts[i] = reader.ReadInt32();
                if (state.unitCosts[i] < 0 || state.unitCosts[i] > 10000) throw new IOException("Invalid unit cost");
            }
            state.nextCommanderId = reader.ReadInt32();
            int count = reader.ReadByte();
            if (state.nextCommanderId < 1 || count > EconomyConfig.MaxCommanders) throw new IOException("Invalid commander roster");
            for (int i = 0; i < count; i++)
            {
                var commander = new CommanderInfo { id = reader.ReadInt32(), type = reader.ReadByte(), status = (CommanderStatus)reader.ReadByte(), crew = reader.ReadByte(), trait = reader.ReadByte(), sellValue = reader.ReadInt32() };
                if (commander.id < 1 || commander.id >= state.nextCommanderId || !CommanderRoster.ValidType(commander.type) ||
                    (byte)commander.status > (byte)CommanderStatus.Deployed || commander.crew < 1 || !CommanderRoster.ValidTrait(commander.trait) ||
                    commander.sellValue < 0 || commander.sellValue > 1000000)
                    throw new IOException("Invalid commander");
                state.commanders.Add(commander);
            }
            return state;
        }
    }
}
