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

        /// <summary>
        /// Something that shouldn't happen (main thread): a warning here, and an error report with everyone in the
        /// lobby's logs (Telemetry.Reports). <paramref name="category"/> groups the reports ("invalid_mastermind_state").
        /// </summary>
        public static void Problem(string category, string message)
        {
            Warn(message);
            Telemetry.Reports.Problem(category, message);
        }
    }
}
