using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace BadNorthMP
{
    /// <summary>
    /// The game's own errors in LogOutput.log: BepInEx leaves Unity's messages out of the file by default
    /// ([Logging.Disk] WriteUnityLog), so an exception that stopped an island loading never showed in the logs players
    /// send. Written straight to the file (the console has them already), each distinct one once, up to Limit.
    /// </summary>
    internal static class GameErrors
    {
        private const int Limit = 40;
        private static readonly HashSet<string> seen = new HashSet<string>();
        private static DiskLogListener disk;

        public static void Capture()
        {
            foreach (ILogListener listener in BepInEx.Logging.Logger.Listeners)
                if (listener is DiskLogListener found)
                {
                    if (found.WriteFromUnityLog) return; // the file has them already
                    disk = found;
                }
            if (disk != null) Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Log || type == LogType.Warning) return;
            if (seen.Count >= Limit || !seen.Add(message + stackTrace) || disk.LogWriter == null) return;
            string level = type == LogType.Exception ? "Error" : type.ToString();
            disk.LogWriter.WriteLine($"[{level,-7}: Unity Log] {message}" + (string.IsNullOrEmpty(stackTrace) ? "" : "\n" + stackTrace.TrimEnd()));
        }
    }
}
