using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
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

        private BattleInfo pending;
        private bool exitRequested;
        private float nextPendingLog;
        private bool loggedReady;
        private bool loadingAnnounced;
        private bool transitioning;
        private bool onBattle;
        private bool returnToMap;
        private BattleInfo latestMap;
        private BattleInfo pendingMap;
        private bool protectedProfile;
        private UserSave savedUser;
        // Campaign loads that failed in a row; after MaxFailures the player is told to restart instead.
        private const int MaxFailures = 3;
        private int failures;
        private bool gaveUp, profileWaitLogged;
        private float nextProfileNotice;

        public static bool ViewingCampaign
        {
            get
            {
                var stack = Singleton<Stack>.instance;
                return Active && Instance && !Instance.onBattle && !Instance.transitioning && !IsLoading() &&
                    stack && stack.stateCampaign.gameObject.activeInHierarchy;
            }
        }

        /// <summary>On the campaign map with nothing more to load (no newer map or battle waiting).</summary>
        public static bool MapSettled => ViewingCampaign && Instance.pendingMap == null && Instance.pending == null && !Instance.returnToMap;

        // Restored when spectating ends.
        private CampaignSave savedCampaign;
        private CampaignSaveMeta savedMeta;
        private ISaveGameHandler savedHandler;

        private static Traverse HandlerField => Traverse.Create(typeof(SaveGameUtilities)).Field("_handler");

        private void Awake() => Instance = this;

        private void Start() => CampaignSync.Init();

        /// <summary>Latest authoritative save; only apply it when no live island is running.</summary>
        public void ViewCampaign(BattleInfo info)
        {
            latestMap = info;
            pendingMap = info;
        }

        /// <summary>Queue a battle to watch. Starts as soon as the player is at the main menu.</summary>
        public void Watch(BattleInfo info)
        {
            if (info == null) return;
            // Reset before the new stream's reliable history arrives. During teardown IslandReady
            // stays false, so those events cannot be applied to the previous island.
            PuppetWorld.Reset();
            returnToMap = false;
            latestMap = new BattleInfo { levelIndex = -1, campaignSave = info.campaignSave };
            pendingMap = null;
            ModLog.Info("Spectator: queued battle, level " + info.levelIndex);
            nextPendingLog = 0f;
            pending = info;
            if (!Active && Singleton<CampaignManager>.instance && Singleton<CampaignManager>.instance.campaign)
                MultiplayerGame.Instance.AddEvent("The defender started a battle. Go back to the main menu to watch it.");
        }

        /// <summary>Leave the island and go back to the main menu.</summary>
        public void Stop()
        {
            if (Active)
                exitRequested = true;
            pending = null;
            pendingMap = latestMap = null;
            returnToMap = false;
            PuppetWorld.Reset();
        }

        public void BattleEnded()
        {
            pending = null;
            PuppetWorld.Reset();
            if (Roles.WatchesBattle)
            {
                returnToMap = Active;
                pendingMap = latestMap;
            }
            else
                Stop();
        }

        private void Update()
        {
            CampaignSync.Update();

            if (Active && !Roles.WatchesBattle && !exitRequested) Stop();
            if (transitioning || IsLoading()) return;

            if (Active && exitRequested)
            {
                BeginTransition(null, leave: true);
                return;
            }

            // Also catch the native map's menu/exit action; do not leave the stub handler installed.
            var currentManager = Singleton<CampaignManager>.instance;
            if (Active && !onBattle && (!currentManager || !currentManager.campaign))
            {
                Restore();
                return;
            }

            // After repeated failures nothing more is loaded until the game restarts (the player was told so).
            if (gaveUp && !Active)
            {
                pending = null;
                pendingMap = null;
                returnToMap = false;
                return;
            }

            // The game loads the player's own profile at its title screen, and generating a campaign before that
            // crashes the game's generator (it reads the profile). A player sent into a lobby at launch waits here.
            if ((pending != null || pendingMap != null || returnToMap) && !Active && !ProfileReady())
            {
                if (!profileWaitLogged)
                {
                    profileWaitLogged = true;
                    ModLog.Info("Spectator: waiting for this player's profile to load before opening the shared campaign");
                }
                if (Time.unscaledTime >= nextProfileNotice)
                {
                    nextProfileNotice = Time.unscaledTime + 4f;
                    BadNorthMP.UI.MapNotice.Show("Press any key to join your friends' game", 4.5f);
                }
                return;
            }
            profileWaitLogged = false;

            if (pending != null && !Active && !AtMainMenu() && Time.unscaledTime >= nextPendingLog)
            {
                nextPendingLog = Time.unscaledTime + 5f;
                var cm = Singleton<CampaignManager>.instance;
                var stack = Singleton<Stack>.instance;
                ModLog.Warn("Spectator waiting: campaignManager=" + (bool)cm +
                    " campaign=" + (cm && cm.campaign) + " stack=" + (bool)stack +
                    " loadingVisible=" + (stack && stack.stateLoading.gameObject.activeInHierarchy) +
                    " levelVisible=" + (stack && stack.stateLevel.gameObject.activeInHierarchy));
            }

            if (pending != null && (Active || AtMainMenu()))
            {
                BattleInfo info = pending;
                pending = null;
                try
                {
                    BadNorthMP.UI.MultiplayerScreen.CloseForBattle();
                    BeginTransition(info);
                }
                catch (Exception e)
                {
                    ModLog.Error($"Couldn't load the defender's battle: {e}");
                    Stop();
                }
                return;
            }

            if ((pendingMap != null || returnToMap) && (!Active ? AtMainMenu() : !onBattle || returnToMap))
            {
                BattleInfo map = pendingMap ?? latestMap;
                pendingMap = null;
                returnToMap = false;
                if (map != null)
                {
                    try
                    {
                        BadNorthMP.UI.MultiplayerScreen.CloseForBattle();
                        BeginTransition(map);
                    }
                    catch (Exception e)
                    {
                        ModLog.Error("Couldn't load the defender's map: " + e);
                        Stop();
                    }
                }
                return;
            }

            if (Active && !loggedReady && IslandReady())
            {
                loggedReady = true;
                BroadcastLoading(false);
                loadingAnnounced = false;
                ModLog.Info("Spectator: live island ready, level " + Singleton<IslandGameplayManager>.instance.island.levelNode.index);
            }

        }

        /// <summary>The player's own profile has loaded (the game does that at its title screen).</summary>
        private static bool ProfileReady() => Profile.userSave != null && !Profile.Loading;

        private static bool IsLoading()
        {
            var stack = Singleton<Stack>.instance;
            return stack && stack.stateLoading.gameObject.activeInHierarchy;
        }

        private static bool AtMainMenu()
        {
            var campaigns = Singleton<CampaignManager>.instance;
            var stack = Singleton<Stack>.instance;
            // GenerateCampaign activates ScenePreloader before resolving its manager.
            // A fresh main menu legitimately has no CampaignManager yet.
            return (!campaigns || !campaigns.campaign) && stack &&
                   !stack.stateLoading.gameObject.activeInHierarchy && !stack.stateLevel.gameObject.activeInHierarchy;
        }

        public static bool IslandReady()
        {
            if (Instance && (Instance.transitioning || Instance.pending != null || Instance.returnToMap || Instance.exitRequested))
                return false;
            var manager = Singleton<IslandGameplayManager>.instance;
            return manager && manager.island && manager.island.state == Island.State.Playing;
        }

        /// <summary>A deep copy of the player's progress. Replaceable for the tests, whose runtime has no BinaryFormatter.</summary>
        internal static Func<UserSave, UserSave> CopyUserSave = save =>
        {
            using (var stream = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(stream, save);
                stream.Position = 0;
                return (UserSave)formatter.Deserialize(stream);
            }
        };

        private void ProtectProfile()
        {
            if (protectedProfile) return;
            // Clone the user progress too: native campaign UI/generation can mutate it in memory.
            UserSave isolated = Profile.userSave != null ? CopyUserSave(Profile.userSave) : null;
            savedCampaign = Profile.campaign;
            savedMeta = Profile.activeCampaignMeta;
            savedUser = Profile.userSave;
            savedHandler = (ISaveGameHandler)HandlerField.GetValue();
            HandlerField.SetValue(new SaveGameHandlerStub());
            protectedProfile = true;
            Profile.userSave = isolated;
            Active = true;
        }

        private void BeginTransition(BattleInfo info, bool leave = false)
        {
            CampaignSave save = leave ? null : info.LoadCampaignSave();
            if (!leave && (info.levelIndex < -1 || save == null || save.levelStates == null ||
                info.levelIndex >= save.levelStates.Count))
                throw new InvalidOperationException("Invalid remote campaign/level");
            ProtectProfile();
            transitioning = true;
            bool battle = !leave && info.levelIndex >= 0;
            loggedReady = false;
            bool failed = false;
            IEnumerator routine = ReplaceCampaign(save);
            try
            {
                if (battle)
                {
                    BroadcastLoading(true);
                    loadingAnnounced = true;
                }
                LoadingScreen.BeginLoadingPhase(leave ? string.Empty : battle ? "Joining the battle" : "Viewing defender campaign", () =>
                {
                    transitioning = false;
                    try
                    {
                        if (failed)
                        {
                            // Retrying every frame would fail the same way each time: give up after a few.
                            if (++failures >= MaxFailures)
                            {
                                GiveUp();
                                return;
                            }
                            // A native leave/shutdown failure may still hold the previous island
                            // or generator threads. Keep save protection until a clean exit finishes.
                            Stop();
                            return;
                        }
                        if (!leave) failures = 0;
                        if (leave)
                        {
                            Singleton<Stack>.instance.stateMeta.SetActive(true);
                            Restore();
                            return;
                        }
                        onBattle = battle;
                        Singleton<Stack>.instance.stateCampaign.SetActive(true);
                        if (battle && !exitRequested && !returnToMap && pending == null)
                        {
                            PuppetWorld.Reset(keepBuffered: true);
                            PlayLevel(info.levelIndex);
                        }
                        else
                        {
                            onBattle = false;
                            if (loadingAnnounced && pending == null)
                            {
                                BroadcastLoading(false, ready: false);
                                loadingAnnounced = false;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        ModLog.Error("Spectator campaign completion failed: " + e);
                        Stop();
                    }
                }, GuardRoutine(routine, () => failed = true));
            }
            catch (Exception e)
            {
                transitioning = false;
                ModLog.Error("Couldn't start spectator campaign loading: " + e);
                Stop();
            }
        }

        private static IEnumerator GuardRoutine(IEnumerator routine, Action failed)
        {
            while (true)
            {
                bool more;
                try { more = routine.MoveNext(); }
                catch (Exception e)
                {
                    ModLog.Error("Spectator campaign transition failed: " + e);
                    failed();
                    yield break;
                }
                if (!more) yield break;
                yield return null;
            }
        }

        private IEnumerator ReplaceCampaign(CampaignSave save)
        {
            SelectionLifecycle.Clear();
            var gameplay = Singleton<IslandGameplayManager>.instance;
            if (gameplay && gameplay.island)
            {
                // Do not run CompleteLevel/AbandonLevel: those change user stats and campaign progress.
                var leave = gameplay.LeaveIslandRoutine(reset: false);
                while (leave.MoveNext()) yield return null;
            }
            onBattle = false;
            var manager = Singleton<CampaignManager>.instance;
            if (manager && manager.campaign)
            {
                var shutdown = manager.islandGenerator.ShutDownRoutine();
                while (shutdown.MoveNext()) yield return null;
                ClearCampaign(manager);
            }
            Singleton<Stack>.instance.stateCampaign.SetActive(false);
            if (save == null) yield break;
            Profile.campaign = save;
            Profile.activeCampaignMeta = save;
            ModLog.Info($"Spectator: loading campaign 0x{save.seed:X8}");
            IEnumerator generate = CampaignManager.GenerateCampaign(save, campaignIsNew: false);
            while (generate.MoveNext()) yield return null;
        }

        /// <summary>
        /// CampaignManager.ClearCampaign, finished by hand when one of the game's exit hooks throws: a campaign whose
        /// generation failed leaves some of them unset (the upgrade screen's portraits). Otherwise that campaign could
        /// never be left, and every attempt failed the same way.
        /// </summary>
        private static void ClearCampaign(CampaignManager manager)
        {
            try
            {
                manager.ClearCampaign();
            }
            catch (Exception e)
            {
                ModLog.Warn("A half-loaded campaign didn't close cleanly; clearing it anyway: " + e.Message);
                Profile.campaign = null;
                var reference = Traverse.Create(manager).Field<RTM.Utilities.WeakReference<Campaign>>("_campaign").Value;
                Campaign broken = reference != null ? reference.Target : null;
                if (reference != null) reference.Target = null;
                try { if (broken) broken.Destroy(); }
                catch (Exception destroy) { ModLog.Warn("Couldn't destroy the half-loaded campaign: " + destroy.Message); }
            }
        }

        /// <summary>The shared campaign failed to load several times in a row: stop trying, put the player's own
        /// saves back, and say so.</summary>
        private void GiveUp()
        {
            ModLog.Error($"Spectator: the shared campaign failed to load {failures} times in a row; giving up until the game restarts");
            failures = 0;
            gaveUp = true;
            pending = null;
            pendingMap = latestMap = null;
            returnToMap = false;
            Restore();
            BadNorthMP.UI.MapNotice.Show("Couldn't load your friends' game. Restart Bad North to rejoin.", 12f);
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

        private static void BroadcastLoading(bool loading, bool ready = true)
        {
            if (Roles.IsMastermind && SteamSession.Instance.InLobby)
                SteamSession.Instance.Broadcast(Packet.Build(MsgType.HostLoading, w => { w.Write(loading); w.Write(ready); }));
        }

        private void Restore()
        {
            if (!protectedProfile) return;
            if (savedHandler != null)
                HandlerField.SetValue(savedHandler);
            Profile.campaign = savedCampaign;
            Profile.activeCampaignMeta = savedMeta;
            Profile.userSave = savedUser;
            savedUser = null;
            protectedProfile = false;
            savedHandler = null;
            savedCampaign = null;
            savedMeta = null;
            if (loadingAnnounced) BroadcastLoading(false, ready: false);
            loadingAnnounced = false;
            SelectionLifecycle.Clear();
            Active = false;
            PuppetWorld.Reset(keepBuffered: pending != null);
            transitioning = false;
            onBattle = false;
            exitRequested = false;
            ModLog.Info("Spectator: back at the main menu, saves restored");
        }

        private void OnDestroy()
        {
            // Unity tears down the gameplay graph on quit/plugin destruction.
            Restore();
            if (Instance == this) Instance = null;
        }
    }
}
