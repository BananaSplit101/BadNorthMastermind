using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BadNorthMP.Telemetry
{
    /// <summary>
    /// PostHog's request bodies: logs as OpenTelemetry JSON (POST /i/v1/logs, the session's meta as the resource),
    /// events as a capture batch (POST /batch/) and the feature flag request (POST /flags?v=2).
    /// </summary>
    internal static class Payloads
    {
        public const string Service = "badnorthmp";

        /// <summary>The log lines among <paramref name="records"/> as one OpenTelemetry logs request.</summary>
        public static string Logs(string metaJson, List<SpoolRecord> records)
        {
            var meta = Json.Parse(metaJson) as Dictionary<string, object> ?? new Dictionary<string, object>();
            var sb = new StringBuilder(4096);
            sb.Append("{\"resourceLogs\":[{\"resource\":{\"attributes\":[");
            Attribute(sb, "service.name", Service, true);
            foreach (KeyValuePair<string, object> pair in meta)
            {
                string key = pair.Key == "version" ? "service.version" : pair.Key == "channel" ? "deployment.environment" : pair.Key;
                if (pair.Value is string || pair.Value is double || pair.Value is bool) Attribute(sb, key, pair.Value, false);
            }
            sb.Append("]},\"scopeLogs\":[{\"scope\":{\"name\":\"").Append(Service).Append("\"},\"logRecords\":[");
            bool first = true;
            foreach (SpoolRecord r in records)
            {
                if (r.isEvent) continue;
                if (!first) sb.Append(',');
                first = false;
                string nanos = (r.unixMs * 1000000L).ToString(CultureInfo.InvariantCulture);
                sb.Append("{\"timeUnixNano\":\"").Append(nanos).Append("\",\"observedTimeUnixNano\":\"").Append(nanos).Append("\",");
                sb.Append("\"severityNumber\":").Append(r.severity.ToString(CultureInfo.InvariantCulture)).Append(",\"severityText\":");
                Json.String(sb, SeverityText(r.severity));
                sb.Append(",\"body\":{\"stringValue\":");
                Json.String(sb, r.text ?? "");
                sb.Append("},\"attributes\":[");
                Attribute(sb, "source", r.source ?? "", true);
                Attribute(sb, "seq", r.seq, false);
                Context(sb, r.context);
                if (!string.IsNullOrEmpty(r.incidents))
                {
                    Attribute(sb, "incident_ids", r.incidents, false);
                    int comma = r.incidents.IndexOf(',');
                    Attribute(sb, "incident_id", comma > 0 ? r.incidents.Substring(0, comma) : r.incidents, false);
                }
                sb.Append("]}");
            }
            sb.Append("]}]}]}");
            return sb.ToString();
        }

        /// <summary>The record's context ("role|phase|lobby|level") as attributes.</summary>
        private static void Context(StringBuilder sb, string context)
        {
            if (string.IsNullOrEmpty(context)) return;
            string[] parts = context.Split('|');
            string[] names = { "role", "phase", "lobby", "level_index" };
            for (int i = 0; i < parts.Length && i < names.Length; i++)
                if (parts[i].Length > 0) Attribute(sb, names[i], parts[i], false);
        }

        public static string SeverityText(int severity) =>
            severity >= 21 ? "FATAL" : severity >= 17 ? "ERROR" : severity >= 13 ? "WARN" : severity <= 5 ? "DEBUG" : "INFO";

        private static void Attribute(StringBuilder sb, string key, object value, bool first)
        {
            if (!first && sb[sb.Length - 1] != '[') sb.Append(',');
            sb.Append("{\"key\":");
            Json.String(sb, key);
            sb.Append(",\"value\":{");
            switch (value)
            {
                case bool b: sb.Append("\"boolValue\":").Append(b ? "true" : "false"); break;
                case long l: sb.Append("\"intValue\":\"").Append(l.ToString(CultureInfo.InvariantCulture)).Append('"'); break;
                case int i: sb.Append("\"intValue\":\"").Append(i.ToString(CultureInfo.InvariantCulture)).Append('"'); break;
                case double d when Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 9e15:
                    sb.Append("\"intValue\":\"").Append(((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture)).Append('"'); break;
                case double d: sb.Append("\"doubleValue\":").Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                default: sb.Append("\"stringValue\":"); Json.String(sb, value?.ToString() ?? ""); break;
            }
            sb.Append("}}");
        }

        /// <summary>The events among <paramref name="records"/> (each already a JSON event) as one capture batch.</summary>
        public static string Batch(string token, List<SpoolRecord> records)
        {
            var sb = new StringBuilder(4096);
            sb.Append("{\"api_key\":");
            Json.String(sb, token);
            sb.Append(",\"batch\":[");
            bool first = true;
            foreach (SpoolRecord r in records)
            {
                if (!r.isEvent || string.IsNullOrEmpty(r.json)) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(r.json);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// One event, ready for a batch: anonymous (no person profile), with its own id (PostHog drops a repeat, so
        /// sending one twice after a failed request does no harm).
        /// </summary>
        public static string Event(string name, string distinctId, Guid uuid, DateTime utc, Dictionary<string, object> properties)
        {
            var props = properties ?? new Dictionary<string, object>();
            props["$process_person_profile"] = false;
            props["$lib"] = Service;
            var ev = new Dictionary<string, object>
            {
                ["uuid"] = uuid.ToString(),
                ["event"] = name,
                ["distinct_id"] = distinctId,
                ["timestamp"] = Json.Iso(utc),
                ["properties"] = props,
            };
            return Json.Write(ev);
        }

        public static string Flags(string token, string distinctId) =>
            Json.Write(new Dictionary<string, object> { ["api_key"] = token, ["distinct_id"] = distinctId });
    }
}
