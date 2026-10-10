using BepInEx.Logging;

namespace BadNorthMP
{
    /// <summary>The mod's lines in the BepInEx log (BepInEx/LogOutput.log).</summary>
    internal static class ModLog
    {
        private static ManualLogSource source;

        public static void Init(ManualLogSource logSource) => source = logSource;

        public static void Info(string message) => source?.LogInfo(message);

        public static void Warn(string message) => source?.LogWarning(message);

        public static void Error(string message) => source?.LogError(message);
    }
}
