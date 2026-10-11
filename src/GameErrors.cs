using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace BadNorthMP
{
    /// <summary>
    /// The game's own errors in the mod's log. BepInEx leaves Unity's messages out of LogOutput.log by default
    /// ([Logging.Disk] WriteUnityLog), so an exception that stopped an island loading never showed in the log players
    /// send. Each distinct error is written once, with its stack trace, and only the first few dozen.
    /// </summary>
    internal static class GameErrors
    {
        private const int Limit = 40;
        private static readonly HashSet<string> seen = new HashSet<string>();

        public static void Capture()
        {
            foreach (ILogListener listener in BepInEx.Logging.Logger.Listeners)
                if (listener is DiskLogListener disk && disk.WriteFromUnityLog) return; // the log has them already
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Log || type == LogType.Warning) return;
            if (seen.Count >= Limit || !seen.Add(message + stackTrace)) return;
            ModLog.Error($"Game error: {message}\n{stackTrace}");
        }
    }
}
