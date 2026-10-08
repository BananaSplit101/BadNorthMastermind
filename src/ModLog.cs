using System.Collections.Generic;
using BepInEx.Logging;

namespace BadNorthMP
{
    /// <summary>BepInEx logging plus a short in-memory history for the on-screen overlay.</summary>
    internal static class ModLog
    {
        private const int MaxRecent = 15;

        private static ManualLogSource source;
        private static readonly Queue<string> recent = new Queue<string>();

        public static IEnumerable<string> Recent => recent;

        public static void Init(ManualLogSource logSource) => source = logSource;

        public static void Info(string message)
        {
            source?.LogInfo(message);
            Remember(message);
        }

        public static void Warn(string message)
        {
            source?.LogWarning(message);
            Remember("[warn] " + message);
        }

        public static void Error(string message)
        {
            source?.LogError(message);
            Remember("[error] " + message);
        }

        private static void Remember(string message)
        {
            recent.Enqueue(message);
            while (recent.Count > MaxRecent)
                recent.Dequeue();
        }
    }
}
