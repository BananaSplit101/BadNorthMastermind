using System;
using System.Threading;
using BadNorthMP.Net;
using BadNorthMP.Spectator;
using UnityEngine;

namespace BadNorthMP.Telemetry
{
    /// <summary>
    /// The bugs that never throw an error:
    /// - a freeze: no frame for over 8 s (not while the game is in the background). A thread of its own notes it in the
    ///   log while it lasts, so a freeze ended from Task Manager still shows in the crash report; once the game is back
    ///   it's reported, and an incident unless it happened while loading;
    /// - waiting for the others to load for over two minutes;
    /// - the mastermind getting no battle updates from the campaign leader for 30 s mid-battle.
    /// </summary>
    internal static class Watchdogs
    {
        public const int FreezeMs = 8000;
        public const float WaitLimit = 120f, SilentStateLimit = 30f;

        /// <summary>How long the players have been waiting for each other to load (Reports.Look sets it).</summary>
        public static float WaitingFor;

        // Shared with the watchdog thread.
        private static volatile int lastBeat;
        private static volatile bool beating, focused = true, unfocusedSinceBeat, running;
        private static volatile string frozenTag;

        private static bool waitReported, silenceReported;

        public static void Start()
        {
            if (running) return;
            running = true;
            new Thread(Watch) { IsBackground = true, Name = "BadNorthMP watchdog" }.Start();
        }

        public static void Stop() => running = false;

        public static void Focus(bool isFocused)
        {
            focused = isFocused;
            if (!isFocused) unfocusedSinceBeat = true;
        }

        /// <summary>Main thread, every frame: a gap since the last frame long enough to be a freeze is reported.</summary>
        public static void Beat()
        {
            int now = Environment.TickCount;
            int gap = unchecked(now - lastBeat);
            bool had = beating, wasAway = unfocusedSinceBeat;
            lastBeat = now;
            beating = true;
            unfocusedSinceBeat = !focused;
            string tagThen = frozenTag;
            frozenTag = null;
            if (!had || wasAway || gap < FreezeMs || Time.realtimeSinceStartup < 30f) return;
            string[] parts = (tagThen ?? Reports.Tag).Split('|');
            string phase = parts.Length > 1 ? parts[1] : "";
            float seconds = gap / 1000f;
            var props = GameContext.Snapshot();
            props["seconds"] = Math.Round(seconds, 1);
            props["phase_then"] = phase;
            Reports.Event("freeze", props);
            ModLog.Warn($"The game froze for {seconds:0.0} s ({phase})");
            if (phase != "loading" && phase != "waiting")
                Incidents.Raise("freeze", $"The game froze for {seconds:0} s ({phase})", false);
        }

        /// <summary>The watchdog's own thread: notes a freeze in the log while it's going on.</summary>
        private static void Watch()
        {
            int noted = 0;
            while (running)
            {
                Thread.Sleep(1000);
                try
                {
                    if (!beating || !focused) continue;
                    int silent = unchecked(Environment.TickCount - lastBeat);
                    if (silent < FreezeMs) { noted = 0; continue; }
                    // Every 30 s while it lasts: the first note at 8 s, then 38 s, 68 s...
                    if (silent / 1000 < noted) continue;
                    if (noted == 0) frozenTag = Reports.Tag;
                    noted = silent / 1000 + 30;
                    Reports.Record(13, "Watchdog", $"The game hasn't shown a frame for {silent / 1000} s ({frozenTag})");
                    Reports.FlushSpool();
                }
                catch (Exception) { }
            }
        }

        /// <summary>Main thread, twice a second: the waits and silences that mean something's stuck.</summary>
        public static void Check()
        {
            try { CheckWaits(); }
            catch (Exception e) { Reports.Record(13, "Error reports", "Watchdog check failed: " + e.GetType().Name + ": " + e.Message); }
        }

        private static void CheckWaits()
        {
            if (WaitingFor <= 0f) waitReported = false;
            else if (WaitingFor > WaitLimit && !waitReported)
            {
                waitReported = true;
                ModLog.Problem("stuck_waiting", $"Waiting for the others to load for {WaitingFor:0} s");
            }

            bool battling = Roles.IsMastermind && SpectatorSession.Active && MastermindNet.HasState &&
                MastermindNet.State.inBattle && !MastermindNet.State.done;
            float silent = Time.unscaledTime - MastermindNet.LastStateTime;
            if (!battling || silent < SilentStateLimit) silenceReported = false;
            else if (!silenceReported)
            {
                silenceReported = true;
                ModLog.Problem("silent_leader", $"No battle updates from the campaign leader for {silent:0} s mid-battle");
            }
        }
    }
}
