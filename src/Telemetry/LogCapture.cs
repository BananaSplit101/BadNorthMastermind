using System;
using BepInEx.Logging;

namespace BadNorthMP.Telemetry
{
    /// <summary>
    /// Every BepInEx log line (the game's own arrive as "Unity Log") into the session's spool, on whatever thread logged
    /// it. Long runs of one line and bursts of over LinesPerSecond are cut short, so a runaway error can't fill the
    /// spool. Mods' error lines are reported as errors too (Unity's come with their stacks through ErrorReporter).
    /// </summary>
    internal sealed class LogCapture : ILogListener
    {
        private const int RepeatsKept = 20, LinesPerSecond = 300;
        public const string UnitySource = "Unity Log";

        private readonly object gate = new object();
        private string lastText, lastSource;
        private int lastSeverity, repeats, secondLines, dropped;
        private long second;

        public void LogEvent(object sender, LogEventArgs e)
        {
            try
            {
                if (e == null) return;
                if ((e.Level & LogLevel.Debug) != 0 && (e.Level & ~LogLevel.Debug) == 0 && !Reports.Verbose) return;
                int severity = Severity(e.Level);
                string source = e.Source?.SourceName ?? "";
                string text = e.Data?.ToString() ?? "";
                if (!Pass(severity, source, text)) return;
                Reports.Record(severity, source, text);
                if (severity >= 17 && source != UnitySource) ErrorReporter.FromLog(source, text);
            }
            catch (Exception) { }
        }

        /// <summary>Whether to keep the line: not the umpteenth repeat in a row, not past the second's budget.</summary>
        private bool Pass(int severity, string source, string text)
        {
            string note = null;
            int noteSeverity = 9;
            lock (gate)
            {
                long now = Reports.NowMs() / 1000;
                if (now != second)
                {
                    if (dropped > 0) note = $"({dropped} log lines left out: more than {LinesPerSecond} in a second)";
                    second = now;
                    secondLines = 0;
                    dropped = 0;
                }
                if (text == lastText && source == lastSource)
                {
                    repeats++;
                    if (repeats > RepeatsKept) return false;
                }
                else
                {
                    if (repeats > RepeatsKept)
                    {
                        note = $"(the line above came {repeats - RepeatsKept} more times)";
                        noteSeverity = lastSeverity;
                    }
                    lastText = text;
                    lastSource = source;
                    lastSeverity = severity;
                    repeats = 0;
                }
                if (++secondLines > LinesPerSecond && severity < 17) { dropped++; return false; }
            }
            if (note != null) Reports.Record(noteSeverity, "Error reports", note);
            return true;
        }

        public static int Severity(LogLevel level)
        {
            if ((level & LogLevel.Fatal) != 0) return 21;
            if ((level & LogLevel.Error) != 0) return 17;
            if ((level & LogLevel.Warning) != 0) return 13;
            if ((level & LogLevel.Message) != 0) return 10;
            if ((level & LogLevel.Info) != 0) return 9;
            return 5;
        }

        public void Dispose() { }
    }
}
