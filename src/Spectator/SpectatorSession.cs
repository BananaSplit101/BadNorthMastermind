using System;
using System.Collections;
using System.IO;
using BadNorthMP.Net;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.CampaignGeneration;
using Voxels.TowerDefense.ProfileInternals;
using Voxels.TowerDefense.UI;
using Stack = Voxels.TowerDefense.Stack;

namespace BadNorthMP.Spectator
{
    /// <summary>
    /// Mastermind side: loads the defender's campaign in memory and opens the same island as a spectator
    /// (no local squads, no local raid), without touching the player's own save files.
    /// </summary>
    internal class SpectatorSession : MonoBehaviour
    {
        public static SpectatorSession Instance { get; private set; }

        /// <summary>True from the moment we start loading the defender's campaign until we're back at the menu.</summary>
        public static bool Active { get; private set; }

        /// <summary>The most recent battle this game played as defender, for solo testing (F11).</summary>
        public static string LastBattlePath => Path.Combine(BepInEx.Paths.BepInExRootPath, "bnmp-lastbattle.bin");

        private BattleInfo pending;
        private bool exitRequested;
        private bool exiting;

        // Restored when spectating ends.
        private CampaignSave savedCampaign;
        private CampaignSaveMeta savedMeta;
        private ISaveGameHandler savedHandler;

        private static Traverse HandlerField => Traverse.Create(typeof(SaveGameUtilities)).Field("_handler");

        // Testing aid: launch with --bnmp-autowatch to spectate the last battle as soon as the main menu
        // is up, log what loaded, and leave again after AutoWatchSeconds.
        private const float AutoWatchSeconds = 20f;
        private bool autoWatchPending;
        private bool autoWatching;
        private float autoStopAt = -1f;

        private void Awake()
        {
            Instance = this;
            autoWatchPending = Array.IndexOf(Environment.GetCommandLineArgs(), "--bnmp-autowatch") >= 0;
        }

        /// <summary>Queue a battle to watch. Starts as soon as the player is at the main menu.</summary>
        public void Watch(BattleInfo info)
        {
            if (Active)
            {
                ModLog.Warn("Already spectating; ignoring new battle");
                return;
            }
            pending = info;
            if (Singleton<CampaignManager>.instance && Singleton<CampaignManager>.instance.campaign)
                MultiplayerGame.Instance.AddEvent("The defender started a battle. Go back to the main menu to watch it.");
        }

        public void WatchLastBattle()
        {
            if (!File.Exists(LastBattlePath))
            {
                ModLog.Warn("No recorded battle yet. Play a battle first, then press F11 at the main menu.");
                return;
            }
            Watch(BattleInfo.FromBytes(File.ReadAllBytes(LastBattlePath)));
        }

        /// <summary>Leave the island and go back to the main menu.</summary>
        public void Stop()
        {
            if (Active)
                exitRequested = true;
            pending = null;
        }

        private void Update()
        {
            UpdateAutoWatch();

            if (pending != null && !Active && AtMainMenu())
            {
                BattleInfo info = pending;
                pending = null;
                try
                {
                    Begin(info);
                }
                catch (Exception e)
                {
                    ModLog.Error($"Couldn't load the defender's battle: {e}");
                    Restore();
                }
            }

            if (Active && exitRequested && !exiting && IslandReady())
            {
                exiting = true;
                ModLog.Info("Spectator: leaving the island");
                Singleton<IslandGameplayManager>.instance.levelLeaver.ExitToMainMenu();
            }

            // ExitToMainMenu clears the campaign; once it's gone we're back at the menu.
            if (Active && exiting && !Singleton<CampaignManager>.instance.campaign)
                Restore();
        }

        private void UpdateAutoWatch()
        {
            if (autoWatchPending && AtMainMenu())
            {
                autoWatchPending = false;
                autoWatching = true;
                ModLog.Info("Auto-watch: spectating the last battle");
                WatchLastBattle();
            }
            if (!autoWatching || !Active)
                return;
            if (autoStopAt < 0 && IslandReady())
            {
                autoStopAt = Time.unscaledTime + AutoWatchSeconds;
                ModLog.Info($"Auto-watch: island is up: {DescribeIsland()}");
            }
            else if (autoStopAt >= 0 && Time.unscaledTime >= autoStopAt)
            {
                autoWatching = false;
                ModLog.Info("Auto-watch: leaving");
                Stop();
            }
        }

        private static string DescribeIsland()
        {
            Island island = Singleton<IslandGameplayManager>.instance.island;
            return $"'{island.name}' level={island.levelNode.index} english={island.english.agents.Count} " +
                   $"vikings={island.vikings.agents.Count} navSpots={island.navSpotter.navSpots.Count} " +
                   $"houses={(island.village ? island.village.houses.Length : 0)} camera={(bool)Singleton<LevelCamera>.instance}";
        }

        private static bool AtMainMenu()
        {
            var campaigns = Singleton<CampaignManager>.instance;
            return campaigns && !campaigns.campaign && Singleton<Stack>.instance && Singleton<Stack>.instance.stateMeta.active;
        }

        private static bool IslandReady()
        {
            var manager = Singleton<IslandGameplayManager>.instance;
            return manager && manager.island && manager.island.state == Island.State.Playing;
        }

        private void Begin(BattleInfo info)
        {
            CampaignSave save = info.LoadCampaignSave();
            if (info.levelIndex < 0 || info.levelIndex >= save.levelStates.Count)
                throw new InvalidOperationException($"level {info.levelIndex} not in campaign ({save.levelStates.Count} levels)");

            // From here on nothing may reach the player's save files.
            savedCampaign = Profile.campaign;
            savedMeta = Profile.activeCampaignMeta;
            savedHandler = (ISaveGameHandler)HandlerField.GetValue();
            HandlerField.SetValue(new SaveGameHandlerStub());
            Active = true;
            exitRequested = false;
            exiting = false;

            Profile.campaign = save;
            Profile.activeCampaignMeta = save;
            ModLog.Info($"Spectator: loading campaign 0x{save.seed:X8}, level {info.levelIndex}");

            int levelIndex = info.levelIndex;
            LoadingScreen.BeginLoadingPhase("Joining the battle", () => PlayLevel(levelIndex), GenerateCampaign(save));
        }

        private static IEnumerator GenerateCampaign(CampaignSave save)
        {
            // Same as Profile.LoadCampaign, minus reading from disk.
            IEnumerator routine = CampaignManager.GenerateCampaign(save, campaignIsNew: false);
            while (routine.MoveNext())
                yield return null;
            Singleton<Stack>.instance.stateCampaign.SetActive(newActive: true);
        }

        private static void PlayLevel(int levelIndex)
        {
            // Copy of the private LevelNodeUIProxy.PlayLevel.
            LevelNode node = Singleton<CampaignManager>.instance.campaign.levels[levelIndex];
            node.islandProxy.preparingToPlay = true;
            node.islandProxy.UpdateBuildPriority();
            if (node.islandProxy.state == IslandProxy.State.None)
                node.islandProxy.GenerateIsland();
            node.Play();
            ModLog.Info($"Spectator: entering level {levelIndex}");
        }

        private void Restore()
        {
            if (savedHandler != null)
                HandlerField.SetValue(savedHandler);
            Profile.campaign = savedCampaign;
            Profile.activeCampaignMeta = savedMeta;
            savedHandler = null;
            savedCampaign = null;
            savedMeta = null;
            Active = false;
            exiting = false;
            exitRequested = false;
            ModLog.Info("Spectator: back at the main menu, saves restored");
        }
    }
}
