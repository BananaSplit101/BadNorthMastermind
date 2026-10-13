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
        // After a battle: wait this long for what comes next (the leader's new map, or a retried battle) before loading
        // the map as it was, which the new one would replace straight away.
        private const float AfterBattleWait = 15f;
        private float afterBattleUntil;
        private BattleInfo latestMap;
        private BattleInfo pendingMap;
        private bool protectedProfile;
        private UserSave savedUser;
        // Campaign loads that failed in a row; after MaxFailures the player is told to restart instead.
        private const int MaxFailures = 3;
        private int failures;
        private bool gaveUp, profileWaitLogged;
        private float nextProfileNotice;
        // The load in progress: which step it's on (for the log), and when it began. A load stuck longer than StuckLimit
        // is abandoned like a failed one; if a battle is still on, the leader is asked for it again (BattleRequest).
        internal const float StuckLimit = 90f;
        private string loadStep = "";
        private float loadStarted = -1f, nextStuckLog;

        /// <summary>The step a load is on (empty when not loading): for error reports.</summary>
        public static string LoadStep => Instance && Instance.loadStarted >= 0f ? Instance.loadStep : "";
        // The leader's battle is under way (it began and hasn't ended), whether or not this player made it in.
        private bool battleUnderway;
        private float nextBattleRequest;

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
            LoadGate.ForgetReport();
            returnToMap = false;
            battleUnderway = true;
            latestMap = new BattleInfo { levelIndex = -1, campaignSave = info.campaignSave };
            pendingMap = null;
            ModLog.Info("Spectator: queued battle, level " + info.levelIndex);
            nextPendingLog = 0f;
            pending = info;
            if (!Active && Singleton<CampaignManager>.instance && Singleton<CampaignManager>.instance.campaign)
                MultiplayerGame.Instance.AddEvent("The defender started a battle. Go back to the main menu to watch it.");
        }

        /// <summary>The player chose to stop watching: leave, and don't ask to be let back into the battle.</summary>
        public void StopWatching()
        {
            battleUnderway = false;
            Stop();
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
            battleUnderway = false;
            LoadGate.ForgetReport();
            PuppetWorld.Reset();
            if (Roles.WatchesBattle)
            {
                returnToMap = Active;
                // A newer map that came in during the battle loads now; otherwise the leader's is on its way.
                afterBattleUntil = pendingMap != null ? 0f : Time.unscaledTime + AfterBattleWait;
            }
            else
                Stop();
        }

        private void Update()
        {
            CampaignSync.Update();

            if (Active && !Roles.WatchesBattle && !exitRequested) Stop();
            if (transitioning && loadStarted >= 0f && Time.unscaledTime - loadStarted > 30f && Time.unscaledTime >= nextStuckLog)
            {
                nextStuckLog = Time.unscaledTime + 15f;
                ModLog.Warn($"Spectator: still loading after {Time.unscaledTime - loadStarted:0} s ({loadStep})");
            }
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

            // The leader's battle is on but this player isn't in it (their load of it got stuck and was abandoned): ask
            // the leader for it again, the way a player who joins mid-battle gets it.
            if (battleUnderway && pending == null && !Active && Roles.WatchesBattle && SteamSession.Instance.InLobby &&
                AtMainMenu() && Time.unscaledTime >= nextBattleRequest)
            {
                nextBattleRequest = Time.unscaledTime + 10f;
                ModLog.Info("Spectator: not in the leader's battle; asking for it again");
                SteamSession.Instance.Send(Roles.DefenderId, Packet.Build(MsgType.BattleRequest));
            }

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
                // The battle ended: the leader's new map (or the retried battle) is on its way.
                if (pendingMap == null && returnToMap && Time.unscaledTime < afterBattleUntil) return;
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
            loadStarted = Time.unscaledTime;
            nextStuckLog = 0f;
            loadStep = "starting";
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
                    loadStarted = -1f;
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
                            if (battle)
                                ModLog.Info($"Spectator: not entering level {info.levelIndex}: " +
                                    (exitRequested ? "leaving" : returnToMap ? "the battle ended meanwhile" : "a newer battle came meanwhile"));
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
                }, GuardRoutine(routine, () => failed = true, () => loadStep));
            }
            catch (Exception e)
            {
                transitioning = false;
                ModLog.Error("Couldn't start spectator campaign loading: " + e);
                Stop();
            }
        }

        /// <summary>
        /// A load that throws, or that's still going after StuckLimit seconds, counts as failed. The game's own campaign
        /// load has waits with no end (for its scene preloader, for its campaign manager): one that never ends would
        /// otherwise leave the player on the loading screen for good.
        /// </summary>
        private static IEnumerator GuardRoutine(IEnumerator routine, Action failed, Func<string> step)
        {
            float started = Time.unscaledTime;
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
                if (Time.unscaledTime - started > StuckLimit)
                {
                    ModLog.Error($"Spectator: loading was stuck {step()} for {StuckLimit:0} s; starting over");
                    failed();
                    yield break;
                }
                yield return null;
            }
        }

        private IEnumerator ReplaceCampaign(CampaignSave save)
        {
            SelectionLifecycle.Clear();
            var gameplay = Singleton<IslandGameplayManager>.instance;
            float began = Time.unscaledTime;
            if (gameplay && gameplay.island)
            {
                loadStep = "leaving the island";
                // Do not run CompleteLevel/AbandonLevel: those change user stats and campaign progress.
                var leave = gameplay.LeaveIslandRoutine(reset: false);
                while (leave.MoveNext()) yield return null;
            }
            onBattle = false;
            var manager = Singleton<CampaignManager>.instance;
            if (manager && manager.campaign)
            {
                loadStep = "stopping the island generator";
                var shutdown = manager.islandGenerator.ShutDownRoutine();
                while (shutdown.MoveNext()) yield return null;
                loadStep = "clearing the campaign";
                ClearCampaign(manager);
            }
            Singleton<Stack>.instance.stateCampaign.SetActive(false);
            if (save == null) yield break;
            Profile.campaign = save;
            Profile.activeCampaignMeta = save;
            // The memory in use, to see in a long session's log whether loads leave things behind.
            ModLog.Info($"Spectator: loading campaign 0x{save.seed:X8} ({GC.GetTotalMemory(false) / (1024 * 1024)} MB in use)");
            loadStep = "generating the campaign (the game's own loader)";
            IEnumerator generate = CampaignManager.GenerateCampaign(save, campaignIsNew: false);
            while (generate.MoveNext()) yield return null;
            loadStep = "done";
            ModLog.Info($"Spectator: campaign loaded in {Time.unscaledTime - began:0.0} s");
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
