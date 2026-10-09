using System;
using System.Collections;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using HarmonyLib;
using Voxels.TowerDefense;
using Voxels.TowerDefense.ProfileInternals;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.Game
{
    /// <summary>
    /// Multiplayer campaigns live in their own save slots (100+, so files "campaign - 100" etc.). The
    /// single-player menus only read slots 0-4, so the two never mix. Everything else (loading, saving,
    /// checkpoints) is the game's own code working on those slot numbers.
    /// </summary>
    internal static class MultiplayerCampaigns
    {
        public const int FirstSlot = 100;
        public const int SlotCount = 5;

        public static readonly CampaignSaveMeta[] Saves = new CampaignSaveMeta[SlotCount];
        public static bool Loaded { get; private set; }

        /// <summary>The next CreateNewCampaign goes into a multiplayer slot (set when we open the new-game popup).</summary>
        public static bool PendingNew { get; set; }
        public static bool OpeningNew { get; private set; }

        public static bool IsMultiplayer(CampaignSaveMeta meta) => meta != null && meta.saveSlot >= FirstSlot;

        public static int FreeSlot
        {
            get
            {
                for (int i = 0; i < SlotCount; i++)
                    if (Saves[i] == null)
                        return FirstSlot + i;
                return -1;
            }
        }

        /// <summary>Read the multiplayer save headers through the game's own save system (Steam Cloud aware).</summary>
        public static IEnumerator LoadHeaders()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                int slot = FirstSlot + i;
                CampaignSaveMeta meta = null;
                var load = SaveGameUtilities.Load(CampaignSave.FileNameGen(slot) + ".meta", result => meta = Deserialize(result.Result), null);
                while (load.MoveNext())
                    yield return null;
                if (meta != null)
                    meta.SetSaveSlot(slot);
                Saves[i] = meta;
            }
            Loaded = true;
        }

        private static CampaignSaveMeta Deserialize(MemoryStream stream)
        {
            if (stream == null || stream.Length == 0)
                return null;
            try
            {
                return (CampaignSaveMeta)new BinaryFormatter().Deserialize(stream);
            }
            catch (Exception e)
            {
                ModLog.Warn($"Unreadable multiplayer save header: {e.Message}");
                return null;
            }
        }

        /// <summary>Opens the game's own new-campaign popup (difficulty, heroes, seed) for a multiplayer slot.</summary>
        public static void StartNew()
        {
            OpeningNew = true;
            try
            {
                NewGameOptionsPopup.OpenMenu();
            }
            finally
            {
                OpeningNew = false;
            }
        }

        public static void Continue(CampaignSaveMeta meta) => MetaMenuHelpers.LoadGame(meta);

        public static void Delete(CampaignSaveMeta meta)
        {
            int index = meta.saveSlot - FirstSlot;
            MetaMenuHelpers.DeleteSave(meta, () =>
            {
                if (index >= 0 && index < SlotCount)
                    Saves[index] = null;
            });
        }

        /// <summary>Keep the list current after the game saves a multiplayer campaign.</summary>
        public static void Remember(CampaignSaveMeta meta)
        {
            int index = meta.saveSlot - FirstSlot;
            if (index >= 0 && index < SlotCount)
                Saves[index] = meta;
        }
    }

    // ---- Hooks into the game's save code -------------------------------------------------------

    [HarmonyPatch(typeof(NewGameOptionsPopup), nameof(NewGameOptionsPopup.OpenMenu))]
    internal static class NewGamePopupOpenPatch
    {
        // Opened by the single-player menus too; only our Play screen marks the campaign as multiplayer.
        private static void Prefix() => MultiplayerCampaigns.PendingNew = MultiplayerCampaigns.OpeningNew;
    }

    [HarmonyPatch(typeof(Profile), nameof(Profile.CreateNewCampaign))]
    internal static class CreateCampaignPatch
    {
        // CreateNewCampaign sets the slot on Profile.activeCampaignMeta (a CampaignSaveMeta made from the save).
        private static void Postfix()
        {
            if (!MultiplayerCampaigns.PendingNew)
                return;
            MultiplayerCampaigns.PendingNew = false;
            int slot = MultiplayerCampaigns.FreeSlot;
            if (slot < 0)
            {
                ModLog.Warn("No free multiplayer save slot; this campaign uses a single-player slot");
                return;
            }
            Profile.activeCampaignMeta.SetSaveSlot(slot);
            MultiplayerCampaigns.Remember(Profile.activeCampaignMeta);
            ModLog.Info($"New multiplayer campaign in save slot {slot}");
        }
    }

    [HarmonyPatch(typeof(Profile), nameof(Profile.SaveCampaign))]
    internal static class SaveCampaignPatch
    {
        // The last line of SaveCampaign records the save in the single-player slot list
        // (meta.campaigns[saveSlot]), which throws for our slots. The files are already written by then.
        private static Exception Finalizer(Exception __exception)
        {
            CampaignSaveMeta active = Profile.activeCampaignMeta;
            if (!MultiplayerCampaigns.IsMultiplayer(active))
                return __exception;
            if (!Spectator.SpectatorSession.Active) // a spectator holds the defender's campaign, not its own
                MultiplayerCampaigns.Remember(active);
            return __exception is ArgumentOutOfRangeException ? null : __exception;
        }
    }

    [HarmonyPatch(typeof(MainMenu), "Play")]
    internal static class MainMenuPlayPatch
    {
        // The regular Play button is single-player.
        private static void Prefix() => MultiplayerCampaigns.PendingNew = false;
    }
}
