using System;
using System.IO;
using BadNorthMP.Net;
using BadNorthMP.Spectator;
using BepInEx;
using Voxels.TowerDefense;

namespace BadNorthMP.Game
{
    /// <summary>Defender-local sidecar. Slot + seed + campaignNumber prevent reused-slot leakage.</summary>
    internal static class MastermindUpgradeSave
    {
        private const int Magic = 0x3155504D;   // v1: bank, levels
        private const int MagicV2 = 0x3255504D; // v2: bank, levels, commanders
        private const int MagicV3 = 0x3355504D; // v3: bank, levels, commanders with traits
        private const int MagicV4 = 0x3455504D; // v4: bank, levels, commanders with traits and prices
        private static string boundPath;
        public static void Reset() => boundPath = null;

        public static void EnsureCampaign()
        {
            if (!Roles.IsDefender || SpectatorSession.Active) return;
            var meta = Profile.activeCampaignMeta;
            if (!MultiplayerCampaigns.IsMultiplayer(meta) || Profile.campaign == null) return;
            string path = Path.Combine(Path.Combine(Paths.ConfigPath, "BadNorthMP-Upgrades"),
                $"{SteamSession.Instance.Me.m_SteamID}-{meta.saveSlot}-{Profile.campaign.seed}-{meta.campaignNumber}.bin");
            if (path == boundPath) return;
            boundPath = path;
            var levels = new byte[MastermindUpgrades.TypeCount * 3];
            float bank = 0f;
            var commanders = new System.Collections.Generic.List<VikingCommander>();
            int nextCommander = 1;
            try
            {
                if (File.Exists(path))
                    using (var reader = new BinaryReader(File.OpenRead(path)))
                    {
                        int magic = reader.ReadInt32();
                        if (magic != Magic && magic != MagicV2 && magic != MagicV3 && magic != MagicV4) throw new IOException("Unknown upgrade save version");
                        bank = reader.ReadSingle();
                        if (float.IsNaN(bank) || float.IsInfinity(bank) || bank < 0f) throw new IOException("Invalid gold bank");
                        for (int i = 0; i < levels.Length; i++)
                        {
                            levels[i] = reader.ReadByte();
                            if (levels[i] > MastermindUpgrades.MaxLevel) throw new IOException("Invalid saved upgrade level");
                        }
                        if (magic != Magic)
                            commanders = CommanderRoster.Read(reader, out nextCommander, traits: magic == MagicV3 || magic == MagicV4, prices: magic == MagicV4);
                        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new IOException("Invalid upgrade save length");
                    }
            }
            catch (Exception e)
            {
                Array.Clear(levels, 0, levels.Length);
                bank = 0f;
                commanders.Clear();
                nextCommander = 1;
                ModLog.Warn("Couldn't load mastermind upgrades: " + e.Message);
            }
            // Commanders from before traits existed get one now (kept from the next save on).
            foreach (VikingCommander commander in commanders)
                if (commander.trait == 0) commander.trait = (int)VikingTraits.Random();
            MastermindSession.RestoreUpgrades(levels, bank);
            MastermindSession.RestoreCommanders(commanders, nextCommander);
        }

        public static bool Save()
        {
            if (!Roles.IsDefender || SpectatorSession.Active) return true;
            if (boundPath == null)
            {
                // Non-multiplayer debug campaigns remain session-only.
                return !MultiplayerCampaigns.IsMultiplayer(Profile.activeCampaignMeta);
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(boundPath));
                string temporary = boundPath + ".tmp";
                using (var writer = new BinaryWriter(File.Create(temporary)))
                {
                    writer.Write(MagicV4);
                    writer.Write(MastermindSession.WarChest);
                    writer.Write(MastermindSession.CopyUpgrades());
                    CommanderRoster.Write(writer, MastermindSession.Commanders, MastermindSession.NextCommanderId);
                }
                if (File.Exists(boundPath)) File.Replace(temporary, boundPath, boundPath + ".bak");
                else File.Move(temporary, boundPath);
                return true;
            }
            catch (Exception e)
            {
                ModLog.Warn("Couldn't save mastermind upgrades: " + e.Message);
                return false;
            }
        }
    }
}
