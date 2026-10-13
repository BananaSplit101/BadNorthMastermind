using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BadNorthMP.Telemetry
{
    /// <summary>One stack frame, as PostHog's error tracking takes it.</summary>
    internal sealed class StackFrame
    {
        public string function = "", module = "", filename;
        public int line;
        public bool inApp;
    }

    /// <summary>
    /// Error text made into what PostHog groups errors by: type, message, frames (newest first) and a fingerprint.
    /// Takes every stack format the game produces: Mono's "Ns.Type.Method (args) &lt;0x1d&gt;", Unity's
    /// "Ns.Type:Method(Args)", .NET's "at Ns.Type.Method () [0x0] in &lt;file&gt;:0", "(wrapper dynamic-method)" frames
    /// and Harmony's patched copies ("DMD&lt;Ns.Type..Method&gt;").
    /// </summary>
    internal static class StackParser
    {
        /// <summary>"InvalidCastException: Cannot cast..." → type and message; anything else is an "Error".</summary>
        public static void SplitMessage(string condition, out string type, out string value)
        {
            condition = (condition ?? "").Trim();
            int newline = condition.IndexOf('\n');
            string first = newline >= 0 ? condition.Substring(0, newline) : condition;
            int colon = first.IndexOf(": ", StringComparison.Ordinal);
            if (colon > 0)
            {
                string head = first.Substring(0, colon);
                if (head.IndexOf(' ') < 0 && (head.EndsWith("Exception") || head.EndsWith("Error") || head.IndexOf('.') > 0))
                {
                    type = head;
                    value = condition.Substring(colon + 2).Trim();
                    return;
                }
            }
            type = "Error";
            value = condition;
        }

        /// <summary>The frames in a stack trace, newest first; lines that aren't frames are skipped.</summary>
        public static List<StackFrame> Parse(string stack)
        {
            var frames = new List<StackFrame>();
            if (string.IsNullOrEmpty(stack)) return frames;
            foreach (string raw in stack.Split('\n'))
            {
                StackFrame frame = ParseLine(raw.Trim());
                if (frame != null) frames.Add(frame);
            }
            return frames;
        }

        public static StackFrame ParseLine(string line)
        {
            if (line.Length == 0 || line.StartsWith("Stack trace", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Rethrow as", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal))
                return null;
            if (line.StartsWith("at ", StringComparison.Ordinal)) line = line.Substring(3);
            if (line.StartsWith("(wrapper", StringComparison.Ordinal))
            {
                int close = line.IndexOf(") ", StringComparison.Ordinal);
                if (close < 0) return null;
                line = line.Substring(close + 2);
            }
            var frame = new StackFrame();
            // Unity's "(at Assets/Foo.cs:42)".
            int at = line.LastIndexOf("(at ", StringComparison.Ordinal);
            if (at >= 0)
            {
                string place = line.Substring(at + 4).TrimEnd(')');
                int colon = place.LastIndexOf(':');
                if (colon > 0)
                {
                    frame.filename = place.Substring(0, colon);
                    int.TryParse(place.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out frame.line);
                }
                line = line.Substring(0, at).TrimEnd();
            }
            // The method: everything before its argument list. Harmony's DMD<...> names can hold brackets themselves.
            string function = line;
            int dmd = line.IndexOf("DMD<", StringComparison.Ordinal);
            int searchFrom = 0;
            if (dmd >= 0)
            {
                int close = line.IndexOf('>', dmd);
                searchFrom = close > 0 ? close : dmd;
            }
            int paren = line.IndexOf('(', searchFrom);
            if (paren > 0) function = line.Substring(0, paren).TrimEnd();
            if (dmd >= 0)
            {
                int start = function.IndexOf("DMD<", StringComparison.Ordinal) + 4;
                int end = function.IndexOf('>', start);
                if (end > start) function = function.Substring(start, end - start).Replace("..", ".");
            }
            function = function.Replace(':', '.').Trim();
            if (function.Length == 0 || function.IndexOf(' ') >= 0 && !function.StartsWith("<")) return null;
            frame.function = function;
            int dot = function.LastIndexOf('.');
            frame.module = dot > 0 ? function.Substring(0, dot) : "";
            frame.inApp = function.StartsWith("BadNorthMP.", StringComparison.Ordinal);
            return frame;
        }

        /// <summary>Frames that are the logging itself, not where the error came from.</summary>
        public static bool IsLogging(StackFrame frame) =>
            frame.function.StartsWith("UnityEngine.Debug", StringComparison.Ordinal) ||
            frame.function.StartsWith("UnityEngine.Logger", StringComparison.Ordinal) ||
            frame.function.StartsWith("UnityEngine.DebugLogHandler", StringComparison.Ordinal) ||
            frame.function.StartsWith("BepInEx.Logging", StringComparison.Ordinal);

        /// <summary>
        /// What makes two errors the same: the type and the top four frames that aren't logging (or, with no stack, the
        /// message with its numbers blanked out), hashed to 16 hex digits. PostHog groups errors by it across versions.
        /// </summary>
        public static string Fingerprint(string type, string value, List<StackFrame> frames)
        {
            var sb = new StringBuilder(type ?? "");
            int used = 0;
            foreach (StackFrame frame in frames)
            {
                if (IsLogging(frame)) continue;
                sb.Append('|').Append(frame.function);
                if (++used == 4) break;
            }
            if (used == 0) sb.Append('|').Append(Normalize(value, 160));
            return Hash64(sb.ToString()).ToString("x16");
        }

        /// <summary>A message with its numbers blanked, so repeats match.</summary>
        public static string Normalize(string message, int max)
        {
            if (string.IsNullOrEmpty(message)) return "";
            var sb = new StringBuilder(Math.Min(message.Length, max));
            bool digits = false;
            foreach (char c in message)
            {
                if (sb.Length >= max) break;
                if (char.IsDigit(c)) { if (!digits) sb.Append('#'); digits = true; continue; }
                digits = false;
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static ulong Hash64(string text)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }
}
