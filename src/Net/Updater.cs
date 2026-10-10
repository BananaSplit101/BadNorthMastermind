using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BadNorthMP.UI;
using Steamworks;
using UnityEngine;
using UnityEngine.Networking;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Versions and updates. Everyone in a lobby must run the same version (each player's is in the lobby's member
    /// data). New versions are the mod's GitHub releases: the newest release's latest.txt says which version it is,
    /// and that release (tag v&lt;version&gt;) carries BadNorthMP.dll. A download must match the size and SHA-256
    /// published with it; it waits next to the game until the player chooses Install and restart. Then the game closes, a small script swaps the
    /// DLL in (keeping the old one as BadNorthMP.dll.previous) and starts the game again through Steam, back into the
    /// same lobby.
    /// </summary>
    internal class Updater : MonoBehaviour
    {
        public const string Repo = "BananaSplit101/BadNorthMastermind";
        // GitHub redirects these to the release's files; UnityWebRequest follows the redirect.
        private const string LatestUrl = "https://github.com/" + Repo + "/releases/latest/download/latest.txt";
        private static string DllUrl(string version) => "https://github.com/" + Repo + "/releases/download/v" + version + "/BadNorthMP.dll";
        private const string VersionKey = "bnmp_client_version";
        private const float RecheckSeconds = 15f * 60f;

        /// <summary>
        /// The lobby can play together: before a campaign, everyone runs this version (it only starts then); during
        /// one, at least the mastermind and the campaign leader do. Anyone else on another version is left out (their
        /// messages ignored, their commanders resting) instead of stopping the game for everybody.
        /// </summary>
        public static bool Compatible { get; private set; }
        /// <summary>What the lobby's versions are, or how the update is going.</summary>
        public static string Status { get; private set; } = "";
        /// <summary>The newest version on GitHub, once known.</summary>
        public static string LatestVersion => latest != null ? latest.version : null;
        public static bool UpdateAvailable => latest != null && ReleaseInfo.Newer(latest.version, Plugin.Version);
        public static bool Busy { get; private set; }
        public static bool Staged { get; private set; }

        private static Updater instance;
        private static ReleaseInfo latest, staged;
        // What to offer next, as a popup on the main menu: download the new version, then restart to install it.
        private enum Offer { None, Download, Install }
        private static Offer offer;
        private static bool offered;
        private float nextCheck, nextScan, nextMenuLook;
        // When each member was first seen without a version: Steam passes it on a moment after they join.
        private static readonly Dictionary<CSteamID, float> unversionedSince = new Dictionary<CSteamID, float>();
        private const float VersionGrace = 10f;
        private CSteamID knownLobby = CSteamID.Nil;

        private static SteamSession Session => SteamSession.Instance;
        private static string StagePath => Path.Combine(BepInEx.Paths.BepInExRootPath, "bnmp-update.dll");
        private static string RejoinPath => Path.Combine(BepInEx.Paths.BepInExRootPath, "bnmp-update-rejoin.txt");
        private static string PluginPath => typeof(Plugin).Assembly.Location;

        private void Awake() => instance = this;

        /// <summary>The lobby to rejoin after an update restarted the game (0: none).</summary>
        public static ulong ConsumeRejoin()
        {
            if (!File.Exists(RejoinPath)) return 0;
            ulong.TryParse(File.ReadAllText(RejoinPath), out ulong id);
            File.Delete(RejoinPath);
            return id;
        }

        public static bool IsCompatible(CSteamID member) => Session.InLobby &&
            SteamMatchmaking.GetLobbyMemberData(Session.Lobby, member, VersionKey) == Plugin.Version;

        private void Update()
        {
            if (!Busy && Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + RecheckSeconds;
                StartCoroutine(Check());
            }
            OfferOnMainMenu();
            if (!Session.Ready || !Session.InLobby)
            {
                Compatible = false;
                knownLobby = CSteamID.Nil;
                return;
            }
            if (knownLobby != Session.Lobby)
            {
                knownLobby = Session.Lobby;
                SteamMatchmaking.SetLobbyMemberData(knownLobby, VersionKey, Plugin.Version);
                // Someone here may already have a version newer than the last check found.
                nextCheck = Mathf.Min(nextCheck, Time.unscaledTime + 2f);
            }
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 1f;
                Scan();
            }
        }

        /// <summary>Does everyone in the lobby run this version? If not, say who runs what.</summary>
        private static void Scan()
        {
            var members = Session.Members();
            var gone = new List<CSteamID>();
            foreach (CSteamID member in unversionedSince.Keys) if (!members.Contains(member)) gone.Add(member);
            foreach (CSteamID member in gone) unversionedSince.Remove(member);
            bool inCampaign = PartyFlow.Active && PartyFlow.State.phase >= 2;
            bool blocked = false, someoneNewer = false;
            var others = new StringBuilder();
            foreach (CSteamID member in members)
            {
                if (member == Session.Me) continue;
                string version = SteamMatchmaking.GetLobbyMemberData(Session.Lobby, member, VersionKey);
                if (version == Plugin.Version) { unversionedSince.Remove(member); continue; }
                if (string.IsNullOrEmpty(version))
                {
                    if (!unversionedSince.TryGetValue(member, out float since)) unversionedSince[member] = since = Time.unscaledTime;
                    if (Time.unscaledTime - since < VersionGrace) continue; // just joined
                }
                if (!inCampaign || member == Roles.MastermindId || member.m_SteamID == PartyFlow.State.authority) blocked = true;
                if (others.Length > 0) others.Append(", ");
                others.Append(SteamSession.NameOf(member)).Append(" has ").Append(string.IsNullOrEmpty(version) ? "no version" : version);
                someoneNewer |= ReleaseInfo.Newer(version, Plugin.Version);
            }
            Compatible = !blocked;
            if (Busy || Staged) return;
            if (others.Length == 0) Status = "Everyone has " + Plugin.Version;
            else if (!blocked) Status = $"{others}: they watch until they update to {Plugin.Version}.";
            else if (someoneNewer) Status = $"{others}. You have {Plugin.Version}: " + (UpdateAvailable ? "update to play." : "an update will show here once it's released on GitHub.");
            else Status = $"{others}. They need to update to {Plugin.Version}.";
        }

        private IEnumerator Check()
        {
            using (var request = UnityWebRequest.Get(LatestUrl + "?t=" + DateTime.UtcNow.Ticks))
            {
                request.timeout = 20;
                yield return request.SendWebRequest();
                if (request.isNetworkError || request.isHttpError)
                {
                    ModLog.Warn("Update check failed: " + request.error);
                    yield break;
                }
                ReleaseInfo info = ReleaseInfo.Parse(request.downloadHandler.text);
                if (info == null)
                {
                    ModLog.Warn("Update check: the newest release's latest.txt is malformed");
                    yield break;
                }
                latest = info;
            }
            if (!UpdateAvailable) yield break;
            ModLog.Info($"Update available: {latest.version} (this is {Plugin.Version})");
            if (offered) yield break;
            offered = true;
            offer = Offer.Download;
            // Mid-game: say so now; the popup waits for the main menu.
            if (!MainMenuFocused())
                MapNotice.Show($"Bad North Multiplayer {latest.version} is out. Update from the main menu or the Multiplayer screen.", 8f);
        }

        /// <summary>Pop the pending offer up once the player is on the main menu with nothing over it.</summary>
        private void OfferOnMainMenu()
        {
            if (offer == Offer.None || Time.unscaledTime < nextMenuLook) return;
            nextMenuLook = Time.unscaledTime + 1f;
            if (!MainMenuFocused()) return;
            Offer showing = offer;
            offer = Offer.None;
            if (showing == Offer.Download) ConfirmDownload();
            else ConfirmInstall();
        }

        /// <summary>The main menu is up and has focus (no screen or popup over it).</summary>
        private static bool MainMenuFocused()
        {
            foreach (Voxels.TowerDefense.UI.MainMenu menu in Resources.FindObjectsOfTypeAll<Voxels.TowerDefense.UI.MainMenu>())
                if (menu && menu.isActiveAndEnabled && menu.isOpen && menu.isFocussed) return true;
            return false;
        }

        public static void ConfirmDownload()
        {
            if (!UpdateAvailable || Busy || Staged) return;
            var popup = Voxels.TowerDefense.UI.ModalOverlay.GetInstance();
            GameUi.InitPopup(popup, "Update to " + latest.version,
                $"Bad North Multiplayer {latest.version} is out (you have {Plugin.Version}). Everyone in a lobby needs the same version.\n\n" +
                $"Download it from github.com/{Repo}/releases? The game stays open until you choose to restart.");
            GameUi.AddPopupButton(popup, "Download", () => instance.StartCoroutine(instance.Download()), closes: true);
            popup.AddCancelButton();
        }

        public static void ConfirmInstall()
        {
            if (!Staged) return;
            var popup = Voxels.TowerDefense.UI.ModalOverlay.GetInstance();
            GameUi.InitPopup(popup, "Update ready",
                $"Bad North Multiplayer {staged.version} is downloaded. Restart the game now to install it? You can also do it later from the Multiplayer screen.");
            GameUi.AddPopupButton(popup, "Install and restart", Install, closes: true);
            popup.AddCancelButton();
        }

        private IEnumerator Download()
        {
            if (Busy || Staged || !UpdateAvailable) yield break;
            ReleaseInfo release = latest;
            Busy = true;
            using (var request = UnityWebRequest.Get(DllUrl(release.version)))
            {
                request.timeout = 120;
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    Status = $"Downloading {release.version}: {Mathf.RoundToInt(request.downloadProgress * 100f)}%";
                    yield return null;
                }
                Busy = false;
                if (request.isNetworkError || request.isHttpError)
                {
                    DownloadFailed("Download failed: " + request.error);
                    yield break;
                }
                byte[] bytes = request.downloadHandler.data;
                if (bytes == null || bytes.Length != release.size || bytes[0] != 'M' || bytes[1] != 'Z' || Hash(bytes) != release.sha256)
                {
                    DownloadFailed("The download didn't match the published release. Try again later.");
                    yield break;
                }
                try { File.WriteAllBytes(StagePath, bytes); }
                catch (Exception e)
                {
                    DownloadFailed("Couldn't save the update: " + e.Message);
                    yield break;
                }
            }
            staged = release;
            Staged = true;
            Status = $"{release.version} is ready. Install and restart when you like.";
            ModLog.Info($"Downloaded {release.version}");
            offer = Offer.Install;
            nextMenuLook = 0f;
        }

        private static void DownloadFailed(string why)
        {
            Status = why;
            ModLog.Warn(why);
            MapNotice.Show(why + " You can try again from the Multiplayer screen.", 8f);
        }

        public static void Install()
        {
            if (!Staged || !File.Exists(StagePath) || Hash(File.ReadAllBytes(StagePath)) != staged.sha256)
            {
                Staged = false;
                Status = "The downloaded update changed on disk. Download it again.";
                return;
            }
            // Every path is this game's own; the download only supplies the DLL's bytes.
            string script;
            using (var stream = typeof(Plugin).Assembly.GetManifestResourceStream("BadNorthMP.UpdateInstall.ps1"))
            using (var reader = new StreamReader(stream)) script = reader.ReadToEnd();
            script = script.Replace("__STAGE__", Quote(StagePath)).Replace("__TARGET__", Quote(PluginPath))
                .Replace("__HASH__", Quote(staged.sha256)).Replace("__PID__", Process.GetCurrentProcess().Id.ToString())
                .Replace("__REJOIN__", Quote(RejoinPath)).Replace("__LOBBY__", Quote(Session.InLobby ? Session.Lobby.m_SteamID.ToString() : "0"));
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            try
            {
                Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded)
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch (Exception e)
            {
                ModLog.Error("Couldn't start the update installer: " + e);
                Status = "Couldn't start the installer. Copy BepInEx/bnmp-update.dll over BepInEx/plugins/BadNorthMP/BadNorthMP.dll by hand.";
                return;
            }
            Application.Quit();
        }

        private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

        private static string Hash(byte[] data)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }
    }
}
