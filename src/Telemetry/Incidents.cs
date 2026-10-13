using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BadNorthMP.Net;
using Steamworks;
using UnityEngine;

namespace BadNorthMP.Telemetry
{
    /// <summary>
    /// "If someone gets a bug, send everyone's logs." Something going wrong (a new error, a problem our checks found, a
    /// freeze, a crash, a bug report) becomes an incident with a code anyone can read out ("7F3K-Q2MD"), and everyone in
    /// the lobby hears of it (MsgType.Incident). Each member, the one it happened to included:
    /// - sends its recent log lines at once, as incident_log events (and again 30 s later, for what happened next);
    /// - sends an incident_member event with its own view of the game then;
    /// - tags its next minute of log lines with the code.
    /// So every side of a multiplayer bug lands in PostHog under one code (tools/fetch-incident.py gets it all back).
    /// Members with error reports turned off send nothing. IncidentRules keeps a bad bug from making incidents nonstop.
    /// </summary>
    internal static class Incidents
    {
        public static readonly IncidentRules Rules = new IncidentRules();
        private const float AftermathAfter = 30f, TaggedFor = 60f;
        private const int LogBytes = 100 * 1024, SummaryLength = 200;

        // What set an incident off, as sent to the lobby (a byte, so it never changes with a version).
        private static readonly string[] Triggers = { "other", "exception", "problem", "watchdog", "bug_report", "crash", "error_flood", "freeze" };

        private sealed class Open
        {
            public string id;
            public float started;
            public long sentUpTo;
            public bool aftermathSent;
        }

        private static readonly System.Random random = new System.Random();
        private static readonly List<Open> open = new List<Open>();
        private static readonly HashSet<string> known = new HashSet<string>();
        private static volatile string tags = "";

        /// <summary>The codes of the incidents going on ("7F3K-Q2MD,..."), stamped on log lines (any thread).</summary>
        public static string Tags => tags;

        /// <summary>
        /// Something went wrong here: the incident it's part of (a new one, or one just started here or in the lobby),
        /// or null when it isn't one (reports off, or too many lately).
        /// </summary>
        public static string Raise(string trigger, string summary, bool manual, Dictionary<string, object> details = null)
        {
            if (!Reports.Collecting) return null;
            // A crash found at startup is about an earlier session: it doesn't count against this one's incidents.
            bool isNew = true;
            string id = trigger == "crash" ? IncidentRules.NewId(random) : Rules.Decide(manual, Time.unscaledTime, random, out isNew);
            if (id == null) return null;
            summary = ErrorReporter.Short(summary, 500);
            var props = GameContext.Snapshot();
            if (details != null) foreach (KeyValuePair<string, object> pair in details) props[pair.Key] = pair.Value;
            props["incident_id"] = id;
            props["trigger"] = trigger;
            props["summary"] = summary;
            props["is_new"] = isNew;
            props["manual"] = manual;
            Reports.Event("incident", props);
            if (isNew && known.Add(id)) Join(id, trigger, summary, null);
            else ModLog.Info($"Incident {id} (part of it): {ErrorReporter.Short(summary, SummaryLength)}");
            return id;
        }

        /// <summary>A bug the player reports themselves (Report bug): its code, or null if they just sent one.</summary>
        public static string ReportBug(string description)
        {
            description = (description ?? "").Trim();
            string id = Raise("bug_report", description.Length > 0 ? description : "Bug report", true,
                new Dictionary<string, object> { ["description"] = description });
            if (id == null) return null;
            var props = GameContext.Snapshot();
            props["incident_id"] = id;
            props["description"] = description;
            Reports.Event("bug_report", props);
            return id;
        }

        /// <summary>A lobby member's incident: we send our side of it.</summary>
        public static void Received(CSteamID sender, BinaryReader reader)
        {
            string id = reader.ReadString();
            byte trigger = reader.ReadByte();
            string summary = reader.ReadString();
            if (!IncidentRules.IsId(id) || !Reports.Collecting || !known.Add(id)) return;
            Rules.Heard(id, Time.unscaledTime);
            string from = Scrub.Label(sender.m_SteamID);
            Join(id, trigger < Triggers.Length ? Triggers[trigger] : "other", ErrorReporter.Short(summary, SummaryLength), from);
        }

        /// <summary>This member's side of an incident: its log and its view now, the lobby told if it started here.</summary>
        private static void Join(string id, string trigger, string summary, string from)
        {
            open.Add(new Open { id = id, started = Time.unscaledTime });
            UpdateTags();
            ModLog.Info($"Incident {id} ({trigger}{(from != null ? ", from " + from : "")}): {ErrorReporter.Short(summary, SummaryLength)}");
            var props = GameContext.Snapshot();
            props["incident_id"] = id;
            props["trigger"] = trigger;
            props["summary"] = summary;
            props["raised_here"] = from == null;
            if (from != null) props["heard_from"] = from;
            Reports.Event("incident_member", props);
            SendLog(open[open.Count - 1], false);
            if (from == null) Tell(id, trigger, summary);
            Reports.FlushSoon();
        }

        private static void Tell(string id, string trigger, string summary)
        {
            SteamSession session = SteamSession.Instance;
            if (!session || !session.InLobby || session.Members().Count < 2) return;
            byte code = (byte)Math.Max(0, Array.IndexOf(Triggers, trigger.StartsWith("problem") ? "problem" : trigger));
            session.Broadcast(Packet.Build(MsgType.Incident, w =>
            {
                w.Write(id);
                w.Write(code);
                w.Write(ErrorReporter.Short(summary, SummaryLength));
            }));
        }

        /// <summary>
        /// The log lines since the last sent for this incident (at first: the recent ones), the newest that fit in
        /// 100 KB, as one incident_log event.
        /// </summary>
        private static void SendLog(Open incident, bool aftermath)
        {
            List<string> lines = Reports.RecentLines(incident.sentUpTo, out long last);
            incident.sentUpTo = last;
            if (lines.Count == 0) return;
            var kept = new List<string>();
            int bytes = 0, left = 0;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                bytes += Encoding.UTF8.GetByteCount(lines[i]) + 1;
                if (bytes > LogBytes) { left = i + 1; break; }
                kept.Add(lines[i]);
            }
            kept.Reverse();
            Reports.Event("incident_log", new Dictionary<string, object>
            {
                ["incident_id"] = incident.id,
                ["aftermath"] = aftermath,
                ["lines"] = kept.Count,
                ["lines_left_out"] = left,
                ["log"] = string.Join("\n", kept.ToArray()),
            });
        }

        /// <summary>Main thread, every frame: aftermaths sent, finished incidents untagged.</summary>
        public static void Tick()
        {
            if (open.Count == 0) return;
            float now = Time.unscaledTime;
            bool changed = false;
            for (int i = open.Count - 1; i >= 0; i--)
            {
                Open incident = open[i];
                if (!incident.aftermathSent && now - incident.started >= AftermathAfter)
                {
                    incident.aftermathSent = true;
                    SendLog(incident, true);
                    Reports.FlushSoon();
                }
                if (now - incident.started >= TaggedFor)
                {
                    open.RemoveAt(i);
                    changed = true;
                }
            }
            if (changed) UpdateTags();
        }

        private static void UpdateTags()
        {
            var ids = new string[open.Count];
            for (int i = 0; i < open.Count; i++) ids[open.Count - 1 - i] = open[i].id; // newest first
            tags = string.Join(",", ids);
        }
    }
}
