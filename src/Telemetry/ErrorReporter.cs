using System;
using System.Collections.Generic;
using UnityEngine;

namespace BadNorthMP.Telemetry
{
    /// <summary>
    /// Errors to PostHog's error tracking: Unity's exceptions and errors (the game's, ours, other mods'), with their
    /// stacks, and mods' error lines, with where they were logged from. Each distinct error (its fingerprint) goes once a
    /// session, with the game's state then; repeats are counted. A new error of ours, or any in a lobby, starts an
    /// incident; one error repeating many times a second (a flood, like the Viking-killer bug) does too.
    /// Errors can come from any thread: they wait in a queue for the main thread (Process).
    /// </summary>
    internal static class ErrorReporter
    {
        private const int QueueLimit = 200, PerFrame = 20, FloodPerSecond = 20;
        private static readonly int[] RepeatMarks = { 10, 100, 1000, 10000 };

        private struct Pending
        {
            public string condition, stack, source;
            public bool handled, synthetic;
        }

        private static readonly object gate = new object();
        private static readonly Queue<Pending> queue = new Queue<Pending>();
        private static int overflow, reported;
        private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        private static readonly Dictionary<string, KeyValuePair<long, int>> perSecond = new Dictionary<string, KeyValuePair<long, int>>();
        private static readonly HashSet<string> flooded = new HashSet<string>();

        public static void Hook() => Application.logMessageReceivedThreaded += OnUnityLog;

        private static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            Enqueue(new Pending { condition = condition, stack = stackTrace, source = "Unity", handled = type != LogType.Exception });
        }

        /// <summary>An error line from a mod (BepInEx): reported with the stack it was logged from.</summary>
        public static void FromLog(string source, string message)
        {
            string stack = "";
            try { stack = new System.Diagnostics.StackTrace(1, false).ToString(); } catch (Exception) { }
            Enqueue(new Pending { condition = message, stack = stack, source = source, handled = true, synthetic = true });
        }

        private static void Enqueue(Pending pending)
        {
            if (!Reports.Collecting) return;
            lock (gate)
            {
                if (queue.Count >= QueueLimit) { overflow++; return; }
                queue.Enqueue(pending);
            }
        }

        /// <summary>Main thread, every frame: a few waiting errors handled.</summary>
        public static void Process()
        {
            for (int i = 0; i < PerFrame; i++)
            {
                Pending pending;
                lock (gate)
                {
                    if (queue.Count == 0) return;
                    pending = queue.Dequeue();
                }
                try { Handle(pending); }
                catch (Exception e) { Reports.Record(13, "Error reports", "Couldn't report an error: " + e.GetType().Name + ": " + e.Message); }
            }
        }

        private static void Handle(Pending p)
        {
            StackParser.SplitMessage(p.condition, out string type, out string value);
            List<StackFrame> frames = Frames(p, ref value);
            if (p.synthetic && type == "Error") type = p.source + " error";
            string fingerprint = StackParser.Fingerprint(type, value, frames);

            counts.TryGetValue(fingerprint, out int count);
            counts[fingerprint] = ++count;
            CheckFlood(fingerprint, type, value);
            if (Array.IndexOf(RepeatMarks, count) >= 0)
                Reports.Event("exception_repeated", new Dictionary<string, object>
                {
                    ["$exception_fingerprint"] = fingerprint, ["type"] = type, ["count"] = count,
                });
            if (count > 1 || reported >= Reports.ExceptionLimit) return;
            reported++;

            bool ours = frames.Exists(f => f.inApp);
            var props = GameContext.Snapshot();
            string incident = null;
            if (ours || props.TryGetValue("members", out object members) && members is int n && n > 1)
                incident = Incidents.Raise("exception", type + ": " + Short(value, 160), false);
            props["$exception_list"] = new List<object> { Exception(type, value, frames, p) };
            props["$exception_fingerprint"] = fingerprint;
            props["$exception_level"] = "error";
            props["error_source"] = p.source;
            props["in_app"] = ours;
            if (incident != null) props["incident_id"] = incident;
            int dropped;
            lock (gate) { dropped = overflow; overflow = 0; }
            if (dropped > 0) props["errors_dropped"] = dropped;
            Reports.Event("$exception", props);
        }

        /// <summary>
        /// The error's frames, newest first, without the logging itself. A mod's error line that holds an exception
        /// (ModLog.Error("...: " + e)) has its exception's stack in the message: that's where it went wrong, so that's used.
        /// </summary>
        private static List<StackFrame> Frames(Pending p, ref string value)
        {
            if (p.synthetic)
            {
                int stackAt = value.IndexOf("\n  at ", StringComparison.Ordinal);
                if (stackAt < 0) stackAt = value.IndexOf("\nat ", StringComparison.Ordinal);
                if (stackAt > 0)
                {
                    List<StackFrame> inMessage = StackParser.Parse(value.Substring(stackAt + 1));
                    if (inMessage.Count > 0)
                    {
                        value = value.Substring(0, stackAt).TrimEnd();
                        return inMessage;
                    }
                }
            }
            List<StackFrame> frames = StackParser.Parse(p.stack);
            int start = 0;
            for (int i = 0; i < frames.Count && i < 16; i++)
                if (Plumbing(frames[i])) start = i + 1;
            return start > 0 ? frames.GetRange(start, frames.Count - start) : frames;
        }

        /// <summary>Frames of the logging and of these reports, above where the error was logged.</summary>
        private static bool Plumbing(StackFrame frame) =>
            StackParser.IsLogging(frame) ||
            frame.function.StartsWith("BadNorthMP.Telemetry.", StringComparison.Ordinal) ||
            frame.function.StartsWith("BadNorthMP.ModLog.", StringComparison.Ordinal) ||
            frame.function.StartsWith("BepInEx.", StringComparison.Ordinal) ||
            frame.function.StartsWith("System.Diagnostics.", StringComparison.Ordinal);

        /// <summary>One error as PostHog's $exception_list takes it: frames oldest first (the error's own frame last).</summary>
        private static Dictionary<string, object> Exception(string type, string value, List<StackFrame> frames, Pending p)
        {
            var list = new List<object>();
            for (int i = frames.Count - 1; i >= 0 && list.Count < 60; i--)
            {
                StackFrame f = frames[i];
                var frame = new Dictionary<string, object>
                {
                    ["platform"] = "custom",
                    ["lang"] = "csharp",
                    ["function"] = f.function,
                    ["module"] = f.module,
                    ["in_app"] = f.inApp,
                    ["resolved"] = true,
                };
                if (!string.IsNullOrEmpty(f.filename)) frame["filename"] = f.filename;
                if (f.line > 0) frame["lineno"] = f.line;
                list.Add(frame);
            }
            return new Dictionary<string, object>
            {
                ["type"] = type,
                ["value"] = Short(value, 2000),
                ["mechanism"] = new Dictionary<string, object> { ["handled"] = p.handled, ["synthetic"] = p.synthetic },
                ["stacktrace"] = new Dictionary<string, object> { ["type"] = "raw", ["frames"] = list },
            };
        }

        /// <summary>The same error many times a second: an incident (once a session for each error).</summary>
        private static void CheckFlood(string fingerprint, string type, string value)
        {
            long now = Reports.NowMs() / 1000;
            perSecond.TryGetValue(fingerprint, out KeyValuePair<long, int> window);
            int inSecond = window.Key == now ? window.Value + 1 : 1;
            perSecond[fingerprint] = new KeyValuePair<long, int>(now, inSecond);
            if (perSecond.Count > 500) perSecond.Clear();
            if (inSecond <= FloodPerSecond || !flooded.Add(fingerprint)) return;
            var props = GameContext.Snapshot();
            props["$exception_fingerprint"] = fingerprint;
            props["type"] = type;
            Reports.Event("error_flood", props);
            Incidents.Raise("error_flood", $"{type} more than {FloodPerSecond} times a second: {Short(value, 160)}", false);
        }

        public static string Short(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= max ? text : text.Substring(0, max) + "...";
        }
    }
}
