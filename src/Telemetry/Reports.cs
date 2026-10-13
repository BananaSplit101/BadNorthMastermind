using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using BadNorthMP.Net;
using BepInEx;
using BepInEx.Bootstrap;
using Steamworks;
using UnityEngine;
using UnityEngine.Networking;
using Voxels.TowerDefense;

namespace BadNorthMP.Telemetry
{
    /// <summary>
    /// Error reports to PostHog (US cloud): the session's log lines (PostHog Logs), each distinct error once a session,
    /// events for what happened, and lobby-wide incidents (Incidents). Sent unless the player turns them off; they're
    /// told once first (ReportsUi's notice). Names, Steam IDs and the Windows user name never leave the computer
    /// (Scrub). Everything goes through the session's spool first (SpoolFile), so playing offline or crashing loses
    /// nothing: what's left is sent at the next launch. PostHog feature flags can switch it all off, turn on more
    /// detailed logging or change the limits.
    /// </summary>
    internal class Reports : MonoBehaviour
    {
        public const string Host = "https://us.i.posthog.com";
        // The project's key: public by design (it can only send data in, never read any out).
        public const string ProjectToken = "phc_mQpzeawrdvo62dVuwkkYKHRsFB8wsXmi2FKMhSBsuNwv";
        private const int UploadBytes = 512 * 1024, RingSize = 600, MaxLine = 64 * 1024;

        public static Reports Instance { get; private set; }
        public static string InstallId { get; private set; } = "";
        public static string SessionId { get; private set; } = "";
        /// <summary>More detailed logging, switched on remotely (the "verbose-logging" flag).</summary>
        public static bool Verbose { get; private set; }
        public static int ExceptionLimit { get; private set; } = 50;
        public static float Fps { get; private set; }
        public static float SlowestFrameMs { get; private set; }
        /// <summary>The line tag ("role|phase|lobby|level") as of the last look, readable from any thread.</summary>
        public static string Tag => lineTag;

        private static SpoolFile spool;
        private static string directory;
        private static volatile string lineTag = "solo|menu||";
        private static readonly object ringGate = new object();
        private static readonly List<KeyValuePair<long, string>> ring = new List<KeyValuePair<long, string>>();
        private static readonly List<string> earlier = new List<string>();
        private static readonly Dictionary<string, float> lastProblem = new Dictionary<string, float>();
        private static bool killed, flagsAsked, flushSoon;
        private static int failures;
        private static float nextUpload, lastUpload = -100f;
        // Per thread: a line logged while recording one isn't recorded (it would recurse).
        [ThreadStatic] private static bool recording;

        /// <summary>The project reports go to (a setting can point a test build elsewhere).</summary>
        public static string Token => ModConfig.ReportToken == null || string.IsNullOrEmpty(ModConfig.ReportToken.Value)
            ? ProjectToken : ModConfig.ReportToken.Value.Trim();

        /// <summary>Collected (kept to send): unless the player turned reports off.</summary>
        public static bool Collecting => spool != null && (ModConfig.SendReports == null || ModConfig.SendReports.Value);

        /// <summary>Sent: collected, the player has seen the notice, and not switched off remotely.</summary>
        public static bool Sending => Collecting && ModConfig.ReportsNoticeShown != null && ModConfig.ReportsNoticeShown.Value &&
            !string.IsNullOrEmpty(Token) && !killed;

        // ---- Starting ------------------------------------------------------------------------------------------------

        /// <summary>From Plugin.Awake, right after the config: from here on every log line and error is kept.</summary>
        public static void Begin()
        {
            try
            {
                if (string.IsNullOrEmpty(ModConfig.ReportInstallId.Value)) ModConfig.ReportInstallId.Value = Guid.NewGuid().ToString("N");
                InstallId = ModConfig.ReportInstallId.Value;
                SessionId = Guid.NewGuid().ToString("N").Substring(0, 12);
                directory = Path.Combine(Paths.BepInExRootPath, "BadNorthMP-logs");
                Directory.CreateDirectory(directory);
                earlier.AddRange(Directory.GetFiles(directory, "session-*.log"));
                earlier.Sort(StringComparer.Ordinal);
                spool = SpoolFile.Create(directory, $"session-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{SessionId.Substring(0, 4)}.log", Json.Write(Meta()));
                SpoolFile.Prune(directory, 10, spool.path);
                earlier.RemoveAll(path => !File.Exists(path));
                BepInEx.Logging.Logger.Listeners.Add(new LogCapture());
                ErrorReporter.Hook();
                Watchdogs.Start();
            }
            catch (Exception e)
            {
                spool = null;
                ModLog.Warn("Error reports couldn't start: " + e.Message);
            }
        }

        /// <summary>The spool header: versions and the system.</summary>
        private static Dictionary<string, object> Meta() => new Dictionary<string, object>
        {
            ["session_id"] = SessionId,
            ["install_id"] = InstallId,
            ["version"] = Plugin.Version,
            ["channel"] = ReleaseChannel.Name,
            ["game_version"] = Application.version,
            ["unity_version"] = Application.unityVersion,
            ["bepinex_version"] = typeof(Paths).Assembly.GetName().Version.ToString(),
            ["os"] = SystemInfo.operatingSystem,
            ["cpu"] = SystemInfo.processorType,
            ["gpu"] = SystemInfo.graphicsDeviceName,
            ["ram_mb"] = SystemInfo.systemMemorySize,
            ["language"] = Application.systemLanguage.ToString(),
            ["started"] = Json.Iso(DateTime.UtcNow),
        };

        private void Awake()
        {
            Instance = this;
            Router.On(MsgType.Incident, Incidents.Received);
            if (spool == null) return;
            try { LookBack(); }
            catch (Exception e) { ModLog.Warn("Error reports: couldn't read earlier sessions: " + e.Message); }
            Event("session_started", SessionDetails());
            StartCoroutine(Uploads());
        }

        /// <summary>The meta plus the mods installed and the settings (mod conflicts, odd settings).</summary>
        private static Dictionary<string, object> SessionDetails()
        {
            Dictionary<string, object> details = Meta();
            var plugins = new List<object>();
            try
            {
                foreach (var plugin in Chainloader.PluginInfos.Values)
                    plugins.Add(plugin.Metadata.Name + " " + plugin.Metadata.Version);
            }
            catch (Exception) { }
            details["plugins"] = plugins;
            details["resolution"] = Screen.width + "x" + Screen.height;
            try
            {
                details["trait_strength"] = ModConfig.TraitDifficulty.Value.ToString();
                details["sail_in"] = ModConfig.SailIn.Value;
                details["max_deployed"] = ModConfig.MaxDeployed.Value;
                details["per_extra_defender"] = ModConfig.PerExtraDefender.Value;
                details["hide_enemy_lineup"] = ModConfig.HideEnemyLineup.Value;
            }
            catch (Exception) { }
            return details;
        }

        /// <summary>
        /// Earlier sessions: each one's end is reported once (closed properly, or a crash with its last lines and the
        /// game's own log from then), and an update since the last one. What they didn't send goes with the uploads.
        /// </summary>
        private static void LookBack()
        {
            string lastVersion = null;
            for (int i = 0; i < earlier.Count; i++)
            {
                string path = earlier[i];
                var meta = Json.Parse(SpoolFile.Meta(path)) as Dictionary<string, object>;
                string version = meta != null && meta.TryGetValue("version", out object v) ? v as string : null;
                if (version != null) lastVersion = version;
                if (File.Exists(path + ".done")) continue;
                bool newest = i == earlier.Count - 1;
                var props = new Dictionary<string, object>
                {
                    ["earlier_session"] = meta != null && meta.TryGetValue("session_id", out object id) ? id : "",
                    ["earlier_version"] = version ?? "",
                };
                if (SpoolFile.Ended(path)) Event("session_ended", props);
                else
                {
                    props["log_tail"] = string.Join("\n", Last(SpoolFile.Tail(path, 48 * 1024), 200).ToArray());
                    if (newest) props["unity_log_tail"] = UnityLogTail(24 * 1024);
                    string code = Incidents.Raise("crash", "The game closed without shutting down (a crash, a freeze ended by force, or the power going)", false, props);
                    ModLog.Info($"Error reports: an earlier session didn't close properly; reported as {code ?? "(not reported: too many)"}");
                }
                try { File.WriteAllText(path + ".done", ""); } catch (IOException) { }
            }
            if (lastVersion != null && lastVersion != Plugin.Version)
                Event("updated", new Dictionary<string, object> { ["from"] = lastVersion, ["to"] = Plugin.Version });
        }

        private static List<string> Last(List<string> lines, int count) => lines.Count <= count ? lines : lines.GetRange(lines.Count - count, count);

        /// <summary>The end of the game's own log from the last run (Unity keeps it as Player-prev.log).</summary>
        private static string UnityLogTail(int maxBytes)
        {
            foreach (string name in new[] { "Player-prev.log", "output_log.txt" })
            {
                try
                {
                    string path = Path.Combine(Application.persistentDataPath, name);
                    if (File.Exists(path)) return SpoolFile.ReadEnd(path, maxBytes, out _);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return "";
        }

        // ---- Recording -----------------------------------------------------------------------------------------------

        public static long NowMs() => (DateTime.UtcNow.Ticks - 621355968000000000L) / 10000L;

        /// <summary>A log line (any thread): kept in the spool and among the recent lines an incident sends.</summary>
        public static void Record(int severity, string source, string text)
        {
            if (recording || !Collecting) return;
            recording = true;
            try
            {
                if (text != null && text.Length > MaxLine) text = text.Substring(0, MaxLine) + " ... (cut short)";
                var record = new SpoolRecord { unixMs = NowMs(), severity = severity, source = source ?? "", context = lineTag, incidents = Incidents.Tags, text = text ?? "" };
                long seq = spool.Append(record, severity >= 17);
                if (seq <= 0) return;
                string readable = SpoolFile.Readable(record);
                lock (ringGate)
                {
                    ring.Add(new KeyValuePair<long, string>(seq, readable));
                    if (ring.Count > RingSize) ring.RemoveRange(0, ring.Count - RingSize);
                }
            }
            catch (Exception) { }
            finally { recording = false; }
        }

        /// <summary>The recent log lines after <paramref name="afterSeq"/> (readable), and the last one's number.</summary>
        public static List<string> RecentLines(long afterSeq, out long lastSeq)
        {
            var lines = new List<string>();
            lastSeq = afterSeq;
            lock (ringGate)
                foreach (KeyValuePair<long, string> line in ring)
                    if (line.Key > afterSeq) { lines.Add(line.Value); lastSeq = line.Key; }
            return lines;
        }

        /// <summary>An event (main thread), tagged with where this player is, sent with the next upload.</summary>
        public static void Event(string name, Dictionary<string, object> properties = null)
        {
            if (!Collecting) return;
            try
            {
                var props = properties ?? new Dictionary<string, object>();
                props["session_id"] = SessionId;
                props["version"] = Plugin.Version;
                props["channel"] = ReleaseChannel.Name;
                string[] parts = lineTag.Split('|');
                props["role"] = parts.Length > 0 ? parts[0] : "";
                props["phase"] = parts.Length > 1 ? parts[1] : "";
                if (parts.Length > 2 && parts[2].Length > 0) props["lobby"] = parts[2];
                if (parts.Length > 3 && parts[3].Length > 0) props["level_index"] = parts[3];
                string json = Payloads.Event(name, InstallId, Guid.NewGuid(), DateTime.UtcNow, props);
                spool.Append(new SpoolRecord { isEvent = true, unixMs = NowMs(), json = json }, true);
                if (name == "$exception" || name.StartsWith("incident") || name == "bug_report") flushSoon = true;
            }
            catch (Exception e) { Debug.LogWarning("[Bad North Multiplayer] Couldn't record " + name + ": " + e.Message); }
        }

        /// <summary>
        /// Something our own checks found wrong (ModLog.Problem, main thread): an event and an incident. The same
        /// category again within 30 s is only in the log.
        /// </summary>
        public static void Problem(string category, string message)
        {
            if (!Collecting) return;
            float now = Time.unscaledTime;
            if (lastProblem.TryGetValue(category, out float last) && now - last < 30f) return;
            lastProblem[category] = now;
            var props = GameContext.Snapshot();
            props["category"] = category;
            props["message"] = message;
            Event("problem", props);
            Incidents.Raise("problem:" + category, message, false);
        }

        /// <summary>Sends what's waiting now rather than at the next regular upload.</summary>
        public static void FlushSoon() => flushSoon = true;

        /// <summary>What's spooled written to disk now (any thread).</summary>
        public static void FlushSpool() => spool?.Flush();

        /// <summary>Reports turned off: what's kept so far is never sent.</summary>
        public static void DiscardUnsent()
        {
            try
            {
                spool?.Flush();
                if (directory == null) return;
                foreach (string path in Directory.GetFiles(directory, "session-*.log")) SpoolFile.Discard(path);
            }
            catch (Exception) { }
        }

        // ---- Every frame ---------------------------------------------------------------------------------------------

        private float nextLook, fpsTime, slowWindow;
        private int fpsFrames;
        private ulong lastLobby;
        private Island lastIsland;
        private float islandSince, waitSince = -1f, nextFriends;
        private bool mismatchReported, updateFailed;

        private void Update()
        {
            if (spool == null) return;
            float dt = Time.unscaledDeltaTime;
            fpsFrames++;
            fpsTime += dt;
            slowWindow = Mathf.Max(slowWindow, dt * 1000f);
            if (fpsTime >= 2f)
            {
                Fps = fpsFrames / fpsTime;
                SlowestFrameMs = slowWindow;
                fpsFrames = 0;
                fpsTime = 0f;
                slowWindow = 0f;
            }
            // Never an error from here every frame: it would report itself over and over.
            try
            {
                Watchdogs.Beat();
                ErrorReporter.Process();
                Incidents.Tick();
                if (Time.unscaledTime >= nextLook)
                {
                    nextLook = Time.unscaledTime + 0.5f;
                    Look();
                    Watchdogs.Check();
                    spool.Flush();
                }
            }
            catch (Exception e)
            {
                if (!updateFailed) Record(13, "Error reports", "Error reports stumbled: " + e);
                updateFailed = true;
            }
#if DEBUG
            // A test error (debug builds only): Ctrl+Shift+F12.
            if (Input.GetKeyDown(KeyCode.F12) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                throw new InvalidOperationException("Test error for the error reports (Ctrl+Shift+F12)");
#endif
        }

        /// <summary>Who's here and what's going on: the line tag, the names to hide, and lobby, island and loading changes.</summary>
        private void Look()
        {
            try
            {
                ulong lobby = GameContext.LobbyId();
                Scrub.Lobby(lobby);
                SteamSession session = SteamSession.Instance;
                if (session && session.Ready)
                {
                    Scrub.Learn(session.Me.m_SteamID, SteamFriends.GetPersonaName());
                    if (session.InLobby)
                        foreach (CSteamID member in session.Members()) Scrub.Learn(member.m_SteamID, SteamSession.NameOf(member));
                    // Friends' names turn up too (their games to join, invites): hidden all the same.
                    if (Time.unscaledTime >= nextFriends)
                    {
                        nextFriends = Time.unscaledTime + 120f;
                        int friends = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
                        for (int i = 0; i < friends && i < 1000; i++)
                        {
                            CSteamID friend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                            Scrub.Learn(friend.m_SteamID, SteamFriends.GetFriendPersonaName(friend));
                        }
                    }
                }
                lineTag = GameContext.Tag();

                if (lobby != lastLobby)
                {
                    if (lastLobby != 0) Event("lobby_left", new Dictionary<string, object> { ["left_lobby"] = Scrub.LobbyLabel(lastLobby) });
                    if (lobby != 0) Event("lobby_joined", GameContext.Snapshot());
                    lastLobby = lobby;
                    mismatchReported = false;
                }
                if (lobby != 0 && !Updater.Compatible && !mismatchReported && session.Members().Count > 1)
                {
                    mismatchReported = true;
                    Event("version_mismatch", GameContext.Snapshot());
                }

                Island island = GameAccess.CurrentIsland;
                if (island != lastIsland)
                {
                    if (lastIsland) Event("island_ended", new Dictionary<string, object> { ["seconds"] = Math.Round(Time.unscaledTime - islandSince, 0) });
                    if (island) Event("island_started", GameContext.Snapshot());
                    lastIsland = island;
                    islandSince = Time.unscaledTime;
                }

                bool waiting = LoadGate.Blocking;
                if (waiting && waitSince < 0f) waitSince = Time.unscaledTime;
                else if (!waiting && waitSince >= 0f)
                {
                    float waited = Time.unscaledTime - waitSince;
                    waitSince = -1f;
                    if (waited > 1f) Event("load_wait", new Dictionary<string, object> { ["seconds"] = Math.Round(waited, 1) });
                }
                Watchdogs.WaitingFor = waitSince >= 0f ? Time.unscaledTime - waitSince : 0f;
            }
            catch (Exception e) { Debug.LogWarning("[Bad North Multiplayer] Error reports: " + e.Message); }
        }

        private void OnApplicationFocus(bool focused) => Watchdogs.Focus(focused);

        private void OnApplicationPause(bool paused) => Watchdogs.Focus(!paused);

        private void OnApplicationQuit()
        {
            Watchdogs.Stop();
            Event("session_closing", new Dictionary<string, object> { ["uptime_s"] = Math.Round(Time.realtimeSinceStartup, 0) });
            spool?.End();
        }

        // ---- Sending -------------------------------------------------------------------------------------------------

        /// <summary>
        /// Every ten seconds (sooner after an error or an incident): the oldest unsent stretch of any session's spool,
        /// its log lines to PostHog Logs and its events to the capture batch; marked sent once both are through.
        /// </summary>
        private IEnumerator Uploads()
        {
            while (true)
            {
                yield return null;
                float now = Time.unscaledTime;
                bool due = now >= nextUpload || (flushSoon && now - lastUpload >= 2f && failures == 0);
                if (!due) continue;
                nextUpload = now + 10f;
                if (!Sending) continue;
                if (!flagsAsked)
                {
                    flagsAsked = true;
                    yield return Flags();
                    if (!Sending) continue;
                }
                flushSoon = false;
                lastUpload = now;
                spool.Flush();
                string path = NextToSend();
                if (path == null) continue;
                // Reading, cleaning and writing the requests happens off the main thread: no hitch in the game.
                var stretch = new Stretch(path, Token);
                ThreadPool.QueueUserWorkItem(_ => stretch.Prepare());
                while (!stretch.Ready) yield return null;
                SpoolFile.Unsent unsent = stretch.unsent;
                if (unsent == null)
                {
                    if (path != spool.path) earlier.Remove(path); // an earlier session's spool that can't be read: given up
                    continue;
                }
                if (unsent.records.Count == 0)
                {
                    SpoolFile.MarkSent(unsent);
                    if (unsent.reachedEnd && path != spool.path) earlier.Remove(path);
                    continue;
                }
                bool ok = true;
                if (stretch.logs != null)
                {
                    var send = new Send(Host + "/i/v1/logs", stretch.logs, true);
                    yield return send.Run();
                    ok = send.Accepted;
                }
                if (ok && stretch.batch != null)
                {
                    var send = new Send(Host + "/batch/", stretch.batch, false);
                    yield return send.Run();
                    ok = send.Accepted;
                }
                if (ok)
                {
                    SpoolFile.MarkSent(unsent);
                    failures = 0;
                    if (!unsent.reachedEnd) nextUpload = Time.unscaledTime + 1f; // more waiting: keep going
                }
                else
                {
                    failures++;
                    nextUpload = Time.unscaledTime + Mathf.Min(300f, 5f * Mathf.Pow(2f, failures - 1));
                }
            }
        }

        /// <summary>
        /// The next stretch of a spool, made ready to send on a worker thread: read, every name and ID taken out, and
        /// written as the two requests (null where it has nothing for one). The main thread reads them once Ready.
        /// </summary>
        private sealed class Stretch
        {
            private readonly string path, token;
            private volatile bool ready;
            public SpoolFile.Unsent unsent;
            public string logs, batch;

            public Stretch(string path, string token) { this.path = path; this.token = token; }

            public bool Ready => ready;

            public void Prepare()
            {
                try
                {
                    unsent = SpoolFile.ReadUnsent(path, UploadBytes);
                    bool anyLines = false, anyEvents = false;
                    foreach (SpoolRecord record in unsent.records)
                    {
                        if (record.isEvent) { record.json = Scrub.CleanJson(record.json); anyEvents = true; }
                        else { record.text = Scrub.Clean(record.text); anyLines = true; }
                    }
                    if (anyLines) logs = Payloads.Logs(Scrub.CleanJson(unsent.meta), unsent.records);
                    if (anyEvents) batch = Payloads.Batch(token, unsent.records);
                }
                catch (Exception) { unsent = null; }
                finally { ready = true; }
            }
        }

        /// <summary>The oldest spool with something left to send (earlier sessions first), or null.</summary>
        private static string NextToSend()
        {
            while (earlier.Count > 0)
            {
                string path = earlier[0];
                if (File.Exists(path))
                {
                    SpoolFile.ReadSent(path, out _, out long offset);
                    if (offset < new FileInfo(path).Length) return path;
                }
                earlier.RemoveAt(0);
            }
            SpoolFile.ReadSent(spool.path, out _, out long sent);
            return sent < new FileInfo(spool.path).Length ? spool.path : null;
        }

        /// <summary>One POST to PostHog; Accepted unless it failed in a way worth trying again (a bad request is dropped).</summary>
        private sealed class Send
        {
            private readonly string url, body;
            private readonly bool bearer;
            public bool Accepted { get; private set; }
            private static bool warned;

            public Send(string url, string body, bool bearer) { this.url = url; this.body = body; this.bearer = bearer; }

            public IEnumerator Run()
            {
                using (var request = new UnityWebRequest(url, "POST"))
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    if (bearer) request.SetRequestHeader("Authorization", "Bearer " + Token);
                    request.timeout = 30;
                    yield return request.SendWebRequest();
                    long code = request.responseCode;
                    if (!request.isNetworkError && code >= 200 && code < 300) { Accepted = true; yield break; }
                    // PostHog turned it down for what it is: sending it again won't help.
                    if (!request.isNetworkError && code >= 400 && code < 500 && code != 408 && code != 429)
                    {
                        Accepted = true;
                        if (!warned)
                        {
                            warned = true;
                            ModLog.Warn($"Error reports: PostHog refused a report ({code}): {Trim(request.downloadHandler?.text)}");
                        }
                    }
                }
            }

            private static string Trim(string text) => string.IsNullOrEmpty(text) ? "" : text.Length > 200 ? text.Substring(0, 200) : text;
        }

        /// <summary>The remote switches (PostHog feature flags), once a session.</summary>
        private static IEnumerator Flags()
        {
            using (var request = new UnityWebRequest(Host + "/flags?v=2", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(Payloads.Flags(Token, InstallId)));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 15;
                yield return request.SendWebRequest();
                if (request.isNetworkError || request.responseCode != 200) yield break;
                Apply(Json.Parse(request.downloadHandler.text));
            }
        }

        /// <summary>A flags answer applied: "telemetry-off", "verbose-logging", and "telemetry-limits" (its JSON payload).</summary>
        internal static void Apply(object flags)
        {
            if (Json.Get(flags, "flags", "telemetry-off", "enabled") is bool off && off)
            {
                killed = true;
                ModLog.Info("Error reports are switched off for now (remotely)");
            }
            if (Json.Get(flags, "flags", "verbose-logging", "enabled") is bool verbose && verbose)
            {
                Verbose = true;
                ModLog.Info("Error reports: detailed logging is on");
            }
            if (Json.Get(flags, "flags", "telemetry-limits", "enabled") is bool limits && limits &&
                Json.Parse(Json.Get(flags, "flags", "telemetry-limits", "metadata", "payload") as string) is Dictionary<string, object> payload)
            {
                if (payload.TryGetValue("auto_gap", out object gap) && gap is double g) Incidents.Rules.AutoGap = (float)g;
                if (payload.TryGetValue("manual_gap", out object manualGap) && manualGap is double m) Incidents.Rules.ManualGap = (float)m;
                if (payload.TryGetValue("merge_window", out object merge) && merge is double w) Incidents.Rules.MergeWindow = (float)w;
                if (payload.TryGetValue("max_auto", out object maxAuto) && maxAuto is double a) Incidents.Rules.MaxAuto = (int)a;
                if (payload.TryGetValue("max_exceptions", out object maxExceptions) && maxExceptions is double e) ExceptionLimit = (int)e;
            }
        }
    }
}
