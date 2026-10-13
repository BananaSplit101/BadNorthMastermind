$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# The error reports' pure parts (no Unity): JSON, scrubbing, the spool, stack parsing, incident rules, PostHog payloads.
$sources = foreach ($file in 'Json', 'Scrub', 'SpoolFile', 'StackParser', 'IncidentRules', 'Payloads') {
    Get-Content -LiteralPath "$root/src/Telemetry/$file.cs" -Raw
}
$test = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BadNorthMP.Telemetry
{
    public static class TelemetryTests
    {
        static int checks;
        static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
        static object Get(object json, params object[] path)
        {
            object at = json;
            foreach (object key in path)
            {
                if (key is string s) { var map = at as Dictionary<string, object>; if (map == null || !map.TryGetValue(s, out at)) return null; }
                else { var list = at as List<object>; int i = (int)key; if (list == null || i >= list.Count) return null; at = list[i]; }
            }
            return at;
        }

        /// <summary>The JSON documents built, for PowerShell's own parser to check too.</summary>
        public static List<string> Documents = new List<string>();

        public static string Run(string temp)
        {
            JsonChecks();
            StackChecks();
            ScrubChecks();
            RuleChecks();
            SpoolChecks(temp);
            PayloadChecks();
            return checks + " telemetry checks passed";
        }

        static void JsonChecks()
        {
            var value = new Dictionary<string, object>
            {
                ["text"] = "quote \" backslash \\ newline \n tab \t bell \u0007 accent \u00e9",
                ["n"] = 3, ["big"] = 1700000000123L, ["f"] = 0.5f, ["nan"] = double.NaN, ["yes"] = true, ["none"] = null,
                ["list"] = new List<object> { 1, "two", new Dictionary<string, object> { ["three"] = 3.25 } },
            };
            string json = Json.Write(value);
            Documents.Add(json);
            Check(json.Contains("\\u0007") && json.Contains("\\n") && json.Contains("\\\""), "control characters, quotes and line breaks are escaped");
            Check(json.Contains("\"nan\":null"), "NaN becomes null (JSON has none)");
            var back = Json.Parse(json) as Dictionary<string, object>;
            Check(back != null && (string)back["text"] == (string)value["text"], "a string comes back exactly");
            Check((double)back["n"] == 3 && (double)back["big"] == 1700000000123d && (bool)back["yes"] && back["none"] == null, "numbers, booleans and null come back");
            Check((double)Get(back, "list", 2, "three") == 3.25, "nested lists and maps come back");
            Check(Json.Parse("{\"a\":1") == null && Json.Parse("nonsense") == null && Json.Parse("") == null && Json.Parse("{} x") == null, "broken JSON is refused");
            Check(Json.Get(Json.Parse("{\"flags\":{\"telemetry-off\":{\"enabled\":true}}}"), "flags", "telemetry-off", "enabled") is bool on && on, "a flag answer is read");
            Check(Json.Get(Json.Parse("{\"flags\":{}}"), "flags", "telemetry-off", "enabled") == null, "a missing flag is null");
            Check(Json.Iso(new DateTime(2026, 10, 13, 9, 5, 7, 42, DateTimeKind.Utc)) == "2026-10-13T09:05:07.042Z", "PostHog timestamps");
        }

        static void StackChecks()
        {
            StackParser.SplitMessage("InvalidCastException: Specified cast is not valid.", out string type, out string message);
            Check(type == "InvalidCastException" && message == "Specified cast is not valid.", "an exception's type and message");
            StackParser.SplitMessage("System.IO.IOException: Invalid mastermind state", out type, out message);
            Check(type == "System.IO.IOException" && message == "Invalid mastermind state", "a full type name");
            StackParser.SplitMessage("The island stopped loading: half way", out type, out message);
            Check(type == "Error" && message == "The island stopped loading: half way", "a sentence isn't a type");

            // Mono's exception stacks (as Unity's player logs them), with a Harmony-patched copy in the middle.
            string mono =
                "  at Voxels.TowerDefense.AgentEnemyFinder.FindTarget (Voxels.TowerDefense.Agent agent) [0x0001d] in <9b2c4d5e6f>:0 \n" +
                "  at (wrapper dynamic-method) Voxels.TowerDefense.Agent.DMD<Voxels.TowerDefense.Agent..UpdateBrain>(Voxels.TowerDefense.Agent)\n" +
                "  at BadNorthMP.Game.VikingKillerPatch.Prefix (Voxels.TowerDefense.Agent __instance) [0x00000] in <filename unknown>:0 \n" +
                "Rethrow as InvalidOperationException: wrapped\n";
            List<StackFrame> frames = StackParser.Parse(mono);
            Check(frames.Count == 3, "three frames, the rethrow line skipped: " + frames.Count);
            Check(frames[0].function == "Voxels.TowerDefense.AgentEnemyFinder.FindTarget" && frames[0].module == "Voxels.TowerDefense.AgentEnemyFinder" && !frames[0].inApp, "a game frame");
            Check(frames[1].function == "Voxels.TowerDefense.Agent.UpdateBrain", "a Harmony-patched method keeps its own name: " + frames[1].function);
            Check(frames[2].function == "BadNorthMP.Game.VikingKillerPatch.Prefix" && frames[2].inApp, "our frames are in-app");

            // Unity's style (Debug.LogError's own frames first, "(at file:line)").
            string unity = "UnityEngine.Debug:LogError(Object)\nBadNorthMP.Net.Router:Dispatch(CSteamID, Byte[]) (at C:/src/Router.cs:42)\nSteamworks.Callback:Run()\n";
            frames = StackParser.Parse(unity);
            Check(frames.Count == 3 && StackParser.IsLogging(frames[0]), "Unity's logging frame is known");
            Check(frames[1].function == "BadNorthMP.Net.Router.Dispatch" && frames[1].filename == "C:/src/Router.cs" && frames[1].line == 42, "Unity frames: names and places");

            // The same error, logged twice with different numbers in it, is one error; another place is another.
            string a = StackParser.Fingerprint("InvalidCastException", "cast 12 failed", StackParser.Parse(mono));
            string b = StackParser.Fingerprint("InvalidCastException", "cast 99 failed", StackParser.Parse(mono));
            string c = StackParser.Fingerprint("InvalidCastException", "cast 12 failed", StackParser.Parse(unity));
            Check(a == b && a != c && a.Length == 16, "fingerprints follow the frames, not the message");
            Check(StackParser.Fingerprint("Error", "Waited 61 s for 2 players", new List<StackFrame>()) ==
                  StackParser.Fingerprint("Error", "Waited 95 s for 3 players", new List<StackFrame>()), "without a stack, the message with its numbers blanked");
            Check(StackParser.Fingerprint("Error", "x", StackParser.Parse(unity)) ==
                  StackParser.Fingerprint("Error", "x", StackParser.Parse("UnityEngine.Debug:LogWarning(Object)\nBadNorthMP.Net.Router:Dispatch(CSteamID, Byte[]) (at C:/src/Router.cs:42)\nSteamworks.Callback:Run()\n")),
                  "logging frames don't change a fingerprint");
            Check(StackParser.Parse(null).Count == 0 && StackParser.Parse("").Count == 0, "no stack, no frames");
            Check(StackParser.Normalize("wave 12 of 30, agent 4567", 100) == "wave # of #, agent #", "numbers blanked");
        }

        static void ScrubChecks()
        {
            const ulong lobby = 109775241234567890UL, jazzy = 76561198000000001UL, jay = 76561198000000002UL, jayden = 76561198000000003UL;
            Scrub.Lobby(lobby);
            string jazzyLabel = Scrub.Learn(jazzy, "JazzyCat");
            string jayLabel = Scrub.Learn(jay, "Jay");
            string jaydenLabel = Scrub.Learn(jayden, "Jayden");
            Check(jazzyLabel == Scrub.LabelFor(jazzy, lobby) && jazzyLabel.StartsWith("player-") && jazzyLabel.Length == 13, "a label is worked out the same on every member's computer");
            Check(jazzyLabel != jayLabel && jayLabel != jaydenLabel, "different players, different labels");
            Check(Scrub.LabelFor(jazzy, lobby + 1) != jazzyLabel, "another lobby, another label (labels can't follow a player around)");
            string line = Scrub.Clean("Jayden and Jay joined JazzyCat's lobby (76561198000000001, 76561198999999999)");
            Check(line == jaydenLabel + " and " + jayLabel + " joined " + jazzyLabel + "'s lobby (" + jazzyLabel + ", steam-id)", "names and Steam IDs: " + line);
            Check(Scrub.Clean("level 76561198000000001234 and 7656119800000000") == "level 76561198000000001234 and 7656119800000000", "longer or shorter numbers aren't Steam IDs");
            Check(Scrub.Clean(@"C:\Users\Bob Smith\AppData\LocalLow\Raw Fury\Bad North\Player.log") == @"C:\Users\<user>\AppData\LocalLow\Raw Fury\Bad North\Player.log", "a Windows user folder");
            Check(Scrub.Clean("C:/users/bob/BadNorth") == "C:/users/<user>/BadNorth", "forward slashes, any case");
            Check(Scrub.Clean(@"C:\\Users\\bob\\x") == @"C:\\Users\\<user>\\x", "an escaped path");
            string me = Environment.UserName;
            if (me.Length >= 3) Check(!Scrub.Clean("D:\\Games\\" + me + "\\Steam").Contains(me), "the Windows user name anywhere");
            Check(Scrub.Clean("") == "" && Scrub.Clean(null) == null, "nothing stays nothing");
            Check(Scrub.LobbyLabel(lobby).StartsWith("lobby-") && !Scrub.LobbyLabel(lobby).Contains(lobby.ToString()) && Scrub.LobbyLabel(0) == "", "a lobby's label isn't its ID");

            // In events: strings cleaned, the JSON still JSON (a name inside an escape can't break it).
            Scrub.Learn(76561198000000004UL, "nt");
            string json = Json.Write(new Dictionary<string, object>
            {
                ["log"] = "JazzyCat said \"hi\"\nthen left; nt", ["path"] = @"C:\Users\Bob\x", ["count"] = 3,
                ["ids"] = new List<object> { "76561198000000002" },
            });
            string cleaned = Scrub.CleanJson(json);
            Documents.Add(cleaned);
            var back = Json.Parse(cleaned) as Dictionary<string, object>;
            Check(back != null, "cleaned JSON is still JSON");
            Check((string)back["log"] == jazzyLabel + " said \"hi\"\nthen left; " + Scrub.Label(76561198000000004UL), "names inside an event: " + back["log"]);
            Check((string)back["path"] == @"C:\Users\<user>\x" && (double)back["count"] == 3 && (string)Get(back, "ids", 0) == jayLabel, "paths, numbers and lists in an event");
            Check(Scrub.CleanJson("not json") == "{}", "unreadable JSON is dropped, not sent as it is");

            // A new lobby: new labels as members are learned again, old names still hidden.
            Scrub.Lobby(lobby + 7);
            Check(!Scrub.Clean("JazzyCat").Contains("JazzyCat"), "names stay hidden after leaving");
            Check(Scrub.Learn(jazzy, "JazzyCat") == Scrub.LabelFor(jazzy, lobby + 7), "relearned in the new lobby: its label");
        }

        static void RuleChecks()
        {
            var rules = new IncidentRules();
            var random = new Random(5);
            string first = rules.Decide(false, 0f, random, out bool isNew);
            Check(first != null && isNew && IncidentRules.IsId(first), "the first problem is an incident: " + first);
            Check(rules.Decide(false, 5f, random, out isNew) == first && !isNew, "another within the merge window joins it");
            Check(rules.Decide(false, 60f, random, out isNew) == null, "too soon after the last: not an incident");
            string second = rules.Decide(false, 130f, random, out isNew);
            Check(second != null && second != first && isNew, "after the gap: a new incident");
            for (int i = 0; i < 3; i++) rules.Decide(false, 300f + i * 200f, random, out isNew);
            Check(rules.Decide(false, 2000f, random, out isNew) == null, "at most five automatic incidents a session");
            string manual = rules.Decide(true, 2001f, random, out isNew);
            Check(manual != null && isNew, "a bug report is always heard, even past the automatic limit");
            Check(rules.Decide(true, 2010f, random, out isNew) == null, "but not twice within 30 s");
            Check(rules.Decide(true, 2040f, random, out isNew) != null, "and again after");

            var heard = new IncidentRules();
            heard.Heard("7F3K-Q2MD", 100f);
            Check(heard.Decide(false, 104f, random, out isNew) == "7F3K-Q2MD" && !isNew, "a problem right after a lobby member's joins theirs");
            Check(heard.Decide(false, 115f, random, out isNew) == null, "and the gap counts from theirs");

            Check(!IncidentRules.IsId("0OI1-L000") && !IncidentRules.IsId("ABCD1234") && !IncidentRules.IsId(null) && !IncidentRules.IsId("ABCD-12345"), "codes from the lobby are checked");
            var seen = new HashSet<string>();
            for (int i = 0; i < 1000; i++) seen.Add(IncidentRules.NewId(random));
            Check(seen.Count == 1000, "codes don't repeat");
        }

        static void SpoolChecks(string temp)
        {
            string dir = Path.Combine(temp, "spool");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            var spool = SpoolFile.Create(dir, "session-20261013-100000-aaaa.log", "{\"version\":\"1.0.18\",\"session_id\":\"abc\"}");
            spool.Append(new SpoolRecord { unixMs = 1000, severity = 9, source = "Bad North Multiplayer", context = "leader|battle|lobby-1|3", text = "first\tline \\ with\nbreaks" }, false);
            spool.Append(new SpoolRecord { isEvent = true, unixMs = 1001, json = "{\"event\":\"x\",\"properties\":{\"t\":\"a\\nb\"}}" }, false);
            spool.Append(new SpoolRecord { unixMs = 1002, severity = 17, source = "Unity Log", incidents = "7F3K-Q2MD", text = "second" }, true);
            var unsent = SpoolFile.ReadUnsent(spool.path, 512 * 1024);
            Check(unsent.records.Count == 3 && unsent.lastSeq == 3 && unsent.reachedEnd && !unsent.ended, "everything written is there to send");
            Check(unsent.records[0].text == "first\tline \\ with\nbreaks" && unsent.records[0].context == "leader|battle|lobby-1|3", "tabs, backslashes and line breaks come back");
            Check(unsent.records[1].isEvent && unsent.records[1].json.Contains("\"a\\nb\""), "an event comes back as written");
            Check(unsent.records[2].severity == 17 && unsent.records[2].incidents == "7F3K-Q2MD", "severity and incident codes");
            Check(Json.Parse(unsent.meta) is Dictionary<string, object> meta && (string)meta["version"] == "1.0.18", "the session's meta");
            SpoolFile.MarkSent(unsent);
            Check(SpoolFile.ReadUnsent(spool.path, 512 * 1024).records.Count == 0, "once sent, not sent again");

            spool.Append(new SpoolRecord { unixMs = 2000, text = "third" }, true);
            // A crash half way through a line: that line isn't sent (it's cut off).
            spool.Flush();
            using (var raw = new FileStream(spool.path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { byte[] half = Encoding.UTF8.GetBytes("L\t5\t2001\t9\tx\t\t\thal"); raw.Write(half, 0, half.Length); }
            unsent = SpoolFile.ReadUnsent(spool.path, 512 * 1024);
            Check(unsent.records.Count == 1 && unsent.records[0].text == "third" && !unsent.reachedEnd, "only whole lines after what was sent; the cut-off one stays");
            SpoolFile.MarkSent(unsent);
            Check(!SpoolFile.Ended(spool.path), "a session still open (or crashed) hasn't ended");
            Check(SpoolFile.Tail(spool.path, 4096).Count == 4 && SpoolFile.Tail(spool.path, 4096)[3].EndsWith("] hal"), "the crash report's tail: the log lines, the cut-off last one too");
            Check(SpoolFile.Tail(spool.path, 4096)[2].EndsWith("[Info: ] third") && SpoolFile.Tail(spool.path, 4096)[1].Contains("[Error: Unity Log] second"), "readable lines: " + SpoolFile.Tail(spool.path, 4096)[1]);

            // A clean end, read by the next launch.
            var closing = SpoolFile.Create(dir, "session-20261013-110000-bbbb.log", "{}");
            closing.Append(new SpoolRecord { unixMs = 3000, text = "bye" }, true);
            closing.End();
            Check(SpoolFile.Ended(closing.path), "a session that closed properly has ended");
            Check(closing.Append(new SpoolRecord { text = "late" }, true) == 0, "nothing after the end");
            unsent = SpoolFile.ReadUnsent(closing.path, 512 * 1024);
            Check(unsent.records.Count == 1 && unsent.ended && unsent.reachedEnd, "its end is seen when sending");
            Check(SpoolFile.Meta(closing.path) == "{}", "meta read back");

            // A line longer than a whole upload is left out, so the rest still goes.
            var huge = SpoolFile.Create(dir, "session-20261013-120000-cccc.log", "{}");
            huge.Append(new SpoolRecord { unixMs = 1, text = new string('x', 3000) }, false);
            huge.Append(new SpoolRecord { unixMs = 2, text = "after" }, true);
            unsent = SpoolFile.ReadUnsent(huge.path, 1024);
            Check(unsent.records.Count == 0 && unsent.endOffset > 3000, "the giant line is skipped");
            SpoolFile.MarkSent(unsent);
            unsent = SpoolFile.ReadUnsent(huge.path, 1024);
            Check(unsent.records.Count == 1 && unsent.records[0].text == "after", "and what follows it is sent");

            // Turned off: everything kept so far counts as sent.
            var off = SpoolFile.Create(dir, "session-20261013-130000-dddd.log", "{}");
            off.Append(new SpoolRecord { unixMs = 1, text = "private" }, true);
            SpoolFile.Discard(off.path);
            Check(SpoolFile.ReadUnsent(off.path, 512 * 1024).records.Count == 0, "discarded: nothing to send");

            // Only the newest sessions are kept (never the current one).
            spool.End(); closing.End(); huge.End(); off.End();
            for (int i = 0; i < 12; i++) File.WriteAllText(Path.Combine(dir, "session-20250101-0000" + i.ToString("00") + "-zzzz.log"), "#SESSION {}\n");
            SpoolFile.WriteSent(Path.Combine(dir, "session-20250101-000001-zzzz.log"), 0, 12);
            string oldest = Path.Combine(dir, "session-20250101-000000-zzzz.log");
            SpoolFile.Prune(dir, 10, oldest);
            Check(Directory.GetFiles(dir, "session-*.log").Length == 11 && File.Exists(oldest) && File.Exists(spool.path) &&
                  !File.Exists(Path.Combine(dir, "session-20250101-000001-zzzz.log")), "ten newest kept, plus the current one");
            Check(!File.Exists(Path.Combine(dir, "session-20250101-000001-zzzz.log.sent")), "a pruned session's marker goes with it");
        }

        static void PayloadChecks()
        {
            var records = new List<SpoolRecord>
            {
                new SpoolRecord { seq = 7, unixMs = 1760000000123, severity = 13, source = "Bad North Multiplayer", context = "mastermind|watching|lobby-1a2b3c4d|5", incidents = "7F3K-Q2MD,AB23-CD45", text = "Quote \" and\nnew line" },
                new SpoolRecord { isEvent = true, seq = 8, json = Payloads.Event("incident", "install-1", new Guid("11111111-2222-3333-4444-555555555555"), new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc), new Dictionary<string, object> { ["incident_id"] = "7F3K-Q2MD" }) },
                new SpoolRecord { seq = 9, unixMs = 1760000000999, severity = 9, source = "Unity Log", text = "plain" },
            };
            string logs = Payloads.Logs("{\"version\":\"1.0.18\",\"channel\":\"public\",\"ram_mb\":16384,\"plugins\":[\"x\"]}", records);
            Documents.Add(logs);
            object parsed = Json.Parse(logs);
            Check(parsed != null, "the logs request is JSON");
            var lines = Get(parsed, "resourceLogs", 0, "scopeLogs", 0, "logRecords") as List<object>;
            Check(lines != null && lines.Count == 2, "log lines only (events go to the batch)");
            Check((string)Get(lines, 0, "body", "stringValue") == "Quote \" and\nnew line" && (string)Get(lines, 0, "severityText") == "WARN" && (double)Get(lines, 0, "severityNumber") == 13, "a line's text and level");
            Check((string)Get(lines, 0, "timeUnixNano") == "1760000000123000000", "nanosecond timestamps as strings");
            var attributes = new Dictionary<string, object>();
            foreach (object attribute in (List<object>)Get(lines, 0, "attributes"))
                attributes[(string)Get(attribute, "key")] = Get(attribute, "value");
            Check((string)Get(attributes["role"], "stringValue") == "mastermind" && (string)Get(attributes["phase"], "stringValue") == "watching" &&
                  (string)Get(attributes["level_index"], "stringValue") == "5", "where the player was");
            Check((string)Get(attributes["incident_id"], "stringValue") == "7F3K-Q2MD" && (string)Get(attributes["incident_ids"], "stringValue") == "7F3K-Q2MD,AB23-CD45", "the incidents going on");
            Check((string)Get(attributes["seq"], "intValue") == "7", "its place in the session");
            var resource = new Dictionary<string, object>();
            foreach (object attribute in (List<object>)Get(parsed, "resourceLogs", 0, "resource", "attributes"))
                resource[(string)Get(attribute, "key")] = Get(attribute, "value");
            Check((string)Get(resource["service.name"], "stringValue") == "badnorthmp" && (string)Get(resource["service.version"], "stringValue") == "1.0.18" &&
                  (string)Get(resource["deployment.environment"], "stringValue") == "public" && (string)Get(resource["ram_mb"], "intValue") == "16384" &&
                  !resource.ContainsKey("plugins"), "the session's resource attributes (scalars only)");

            string batch = Payloads.Batch("phc_test", records);
            Documents.Add(batch);
            object parsedBatch = Json.Parse(batch);
            Check((string)Get(parsedBatch, "api_key") == "phc_test" && ((List<object>)Get(parsedBatch, "batch")).Count == 1, "events only in the batch");
            Check((string)Get(parsedBatch, "batch", 0, "event") == "incident" && (string)Get(parsedBatch, "batch", 0, "uuid") == "11111111-2222-3333-4444-555555555555" &&
                  (string)Get(parsedBatch, "batch", 0, "timestamp") == "2026-10-13T00:00:00.000Z" && (string)Get(parsedBatch, "batch", 0, "distinct_id") == "install-1", "an event's id, time and install");
            Check(Get(parsedBatch, "batch", 0, "properties", "$process_person_profile") is bool profile && !profile && (string)Get(parsedBatch, "batch", 0, "properties", "$lib") == "badnorthmp", "anonymous events");
            Check(Payloads.Batch("phc_test", new List<SpoolRecord>()) == "{\"api_key\":\"phc_test\",\"batch\":[]}", "an empty batch");
            string flags = Payloads.Flags("phc_test", "install-1");
            Documents.Add(flags);
            Check((string)Get(Json.Parse(flags), "distinct_id") == "install-1", "the flags request");
            Check(Payloads.SeverityText(21) == "FATAL" && Payloads.SeverityText(17) == "ERROR" && Payloads.SeverityText(9) == "INFO" && Payloads.SeverityText(5) == "DEBUG", "severity names");
        }
    }
}
'@
# One compilation unit: every file's usings go to the top.
$all = ($sources + $test) -join "`n"
$usings = [regex]::Matches($all, '(?m)^using [\w.]+;\s*$') | ForEach-Object { $_.Value.Trim() } | Sort-Object -Unique
Add-Type -TypeDefinition (($usings -join "`n") + "`n" + [regex]::Replace($all, '(?m)^using [\w.]+;\s*$', ''))
$temp = Join-Path ([System.IO.Path]::GetTempPath()) "badnorthmp-telemetry-test"
New-Item -ItemType Directory -Force $temp | Out-Null
$result = [BadNorthMP.Telemetry.TelemetryTests]::Run($temp)
# A second, independent JSON parser: PowerShell's own must read every document too.
foreach ($document in [BadNorthMP.Telemetry.TelemetryTests]::Documents) { $null = $document | ConvertFrom-Json }
Remove-Item -Recurse -Force $temp
Write-Output "$result; $([BadNorthMP.Telemetry.TelemetryTests]::Documents.Count) documents valid JSON for PowerShell too"
