using System;
using System.Collections.Generic;
using System.IO;
using BadNorthMP.Spectator;
using Steamworks;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Net
{
    /// <summary>Reliable, bounded campaign snapshots, separate from the island battle stream.</summary>
    internal static class CampaignSync
    {
        private const int ChunkSize = 48 * 1024;
        private const int MaxPayload = 16 * 1024 * 1024;
        private static bool initialized, dirty = true, quiet;
        private static readonly HashSet<CSteamID> resendTo = new HashSet<CSteamID>();
        private static float nextPoll, receiveDeadline;
        private static CSteamID lobby = CSteamID.Nil, defender = CSteamID.Nil;
        private static Voxels.TowerDefense.ProfileInternals.CampaignSave observedCampaign;
        private static byte[] snapshot;
        private static readonly HashSet<CSteamID> sentTo = new HashSet<CSteamID>();
        private static int revision, receivedRevision, partCount, receivedCount;
        /// <summary>Leader: the latest campaign every viewer was sent and has to load (not quiet refreshes).</summary>
        public static int LoudRevision { get; private set; }
        /// <summary>Watchers: the latest campaign received whole and handed to the map.</summary>
        public static int AppliedRevision { get; private set; }
        // Watchers: whether a campaign arrived since the party started, and when to ask again if not.
        private static bool haveCampaign;
        private static float nextRequest;
        private static byte[][] parts;

        public static void Init()
        {
            if (initialized) return;
            initialized = true;
            Router.On(MsgType.CampaignChunk, Receive);
            Router.On(MsgType.CampaignRequest, (sender, reader) =>
            {
                var session = SteamSession.Instance;
                if (!session || !Roles.IsDefender || !session.Members().Contains(sender)) return;
                ModLog.Info(SteamSession.NameOf(sender) + " hasn't got the campaign; sending it again");
                sentTo.Remove(sender);
                nextPoll = 0f;
            });
            SteamSession.Instance.ConnectionReset += member =>
            {
                // The session may have swallowed the snapshot: send it again.
                if (Roles.IsDefender) { sentTo.Remove(member); nextPoll = 0f; }
            };
            Router.On(MsgType.CampaignClosed, (sender, reader) =>
            {
                if (!Accept(sender)) return;
                int id = reader.ReadInt32();
                if (id <= receivedRevision) return;
                receivedRevision = id;
                parts = null;
                SpectatorSession.Instance.Stop();
            });
        }

        // Changes come in bursts (an island's end saves, the map opens, the menu reopens, ...) and every snapshot makes
        // every viewer reload their map, so the leader sends once the campaign has been still, and on the map, for
        // Settle seconds: at most MaxSettle seconds after the first change or the map coming up, whichever is later (an
        // island's end saves before a load that can take longer than that). The map's own background saves (islands
        // built as they come within reach) are quiet: every viewer's map does that itself (IslandGeneratorQuietSavePatch).
        private const float Settle = 1.5f, MaxSettle = 6f;
        private static float changedAt, firstChangeAt = -1f, mapSince = -1f;

        private static void Changed()
        {
            changedAt = Time.unscaledTime;
            if (firstChangeAt < 0f) firstChangeAt = changedAt;
        }

        public static void MarkDirty() { dirty = true; Changed(); }

        /// <summary>
        /// The campaign changed in a way every viewer already applied (a broadcast upgrade). Refresh the snapshot
        /// for players who join later, without making current viewers reload their map.
        /// </summary>
        public static void MarkQuiet() { quiet = true; Changed(); }

        /// <summary>Send one viewer the current campaign again, e.g. after their local upgrade was refused.</summary>
        public static void Resend(CSteamID member) { quiet = true; resendTo.Add(member); }

        /// <summary>The leader has more changes on the way: hold the snapshot back a moment (see Settle).</summary>
        private static bool Settling()
        {
            if (snapshot == null || (!dirty && !quiet) || firstChangeAt < 0f) return false;
            float now = Time.unscaledTime;
            if (now - Math.Max(firstChangeAt, mapSince) >= MaxSettle) return false;
            return now - changedAt < Settle || now - mapSince < Settle;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>True while a save should only refresh the snapshot quietly (see MarkQuiet).</summary>
        public static bool QuietSave;

        private static bool Accept(CSteamID sender)
        {
            RefreshContext();
            return SteamSession.Instance && SteamSession.Instance.InLobby && Roles.WatchesBattle && sender == Roles.DefenderId;
        }

        public static void Update()
        {
            var session = SteamSession.Instance;
            if (!session) return;
            RefreshContext();
            if (parts != null && Time.unscaledTime > receiveDeadline) parts = null;
            AskIfMissing(session);
            if (!session.InLobby || !Roles.IsDefender || !Updater.Compatible ||
                SpectatorSession.Active || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.5f;
            var stack = Singleton<Voxels.TowerDefense.Stack>.instance;
            var manager = Singleton<CampaignManager>.instance;
            // SaveCampaign runs before LeaveIsland and before next-turn animation finishes.
            // Wait until the native map is present; never replace a running remote island.
            if (!stack || stack.stateLoading.gameObject.activeInHierarchy || BattleStream.Active) return;
            var campaign = manager && manager.campaign ? Profile.campaign : null;
            if (campaign != observedCampaign)
            {
                observedCampaign = campaign;
                dirty = true;
                Changed();
            }
            var members = session.Members();
            sentTo.RemoveWhere(member => !members.Contains(member) || !Updater.IsCompatible(member));
            if (campaign == null)
            {
                if (dirty)
                {
                    snapshot = null;
                    ++revision;
                    sentTo.Clear();
                    dirty = false;
                }
                foreach (var member in members)
                    if (member != session.Me && Updater.IsCompatible(member) && sentTo.Add(member))
                        session.Send(member, Packet.Build(MsgType.CampaignClosed, w => w.Write(revision)));
                return;
            }
            if (!stack.stateCampaign.gameObject.activeInHierarchy) { mapSince = -1f; return; }
            if (mapSince < 0f) mapSince = Time.unscaledTime;
            if (Settling()) return;
            if (dirty || quiet || snapshot == null)
            {
                try
                {
                    byte[] captured = BattleInfo.Capture(-1).ToBytes();
                    if (captured.Length > MaxPayload) throw new InvalidOperationException("Campaign snapshot exceeds 16 MiB");
                    // Nothing actually changed (the map reopening after a menu, a save of the same state): no reload for anyone.
                    if (Same(captured, snapshot))
                        foreach (var member in resendTo) sentTo.Remove(member);
                    else
                    {
                        snapshot = captured;
                        ++revision;
                        // A quiet refresh only goes to players who don't have the change yet.
                        if (dirty || !quiet) { sentTo.Clear(); LoudRevision = revision; }
                        else foreach (var member in resendTo) sentTo.Remove(member);
                    }
                    resendTo.Clear();
                    dirty = quiet = false;
                    firstChangeAt = -1f;
                }
                catch (Exception e)
                {
                    snapshot = null;
                    ModLog.Problem("campaign_sync_capture", "Campaign sync capture failed: " + e);
                    nextPoll = Time.unscaledTime + 5f;
                    return;
                }
            }
            foreach (var member in members)
                if (member != session.Me && Updater.IsCompatible(member) && sentTo.Add(member))
                    Send(member, snapshot, revision);
        }

        /// <summary>Watchers: the campaign hasn't arrived 10 s after the party started (and no battle is showing): ask every 10 s.</summary>
        private static void AskIfMissing(SteamSession session)
        {
            if (haveCampaign || !session.InLobby || Roles.IsDefender || !Roles.WatchesBattle || !Updater.Compatible ||
                SpectatorSession.Active || PartyFlow.StartedAt <= 0f) return;
            if (Time.unscaledTime - PartyFlow.StartedAt < 10f || Time.unscaledTime < nextRequest) return;
            nextRequest = Time.unscaledTime + 10f;
            CSteamID leader = Roles.DefenderId;
            if (leader == CSteamID.Nil || !session.Members().Contains(leader)) return;
            ModLog.Info("The campaign hasn't arrived from " + SteamSession.NameOf(leader) + "; asking again");
            session.Send(leader, Packet.Build(MsgType.CampaignRequest));
        }

        private static void RefreshContext()
        {
            var session = SteamSession.Instance;
            if (!session) return;
            CSteamID currentDefender = Roles.DefenderId;
            if (lobby != session.Lobby || defender != currentDefender)
            {
                // Pending network campaign/battle work belongs to the previous lobby/defender.
                if (lobby != CSteamID.Nil && SpectatorSession.Instance)
                    SpectatorSession.Instance.Stop();
                lobby = session.Lobby;
                defender = currentDefender;
                observedCampaign = null;
                snapshot = null;
                sentTo.Clear();
                dirty = true;
                parts = null;
                receivedRevision = 0;
                AppliedRevision = LoudRevision = 0;
                nextPoll = 0;
                haveCampaign = false;
                nextRequest = 0f;
            }
        }

        private static void Send(CSteamID recipient, byte[] payload, int id)
        {
            int count = (payload.Length + ChunkSize - 1) / ChunkSize;
            for (int index = 0; index < count; index++)
            {
                int offset = index * ChunkSize;
                int size = Math.Min(ChunkSize, payload.Length - offset);
                SteamSession.Instance.Send(recipient, Packet.Build(MsgType.CampaignChunk, writer =>
                {
                    writer.Write(id);
                    writer.Write(index);
                    writer.Write(count);
                    writer.Write(size);
                    writer.Write(payload, offset, size);
                }));
            }
        }

        private static void Receive(CSteamID sender, BinaryReader reader)
        {
            if (!Accept(sender)) return;
            try
            {
                int id = reader.ReadInt32(), index = reader.ReadInt32(), count = reader.ReadInt32(), size = reader.ReadInt32();
                if (id < receivedRevision || (id == receivedRevision && parts == null)) return;
                if (id <= 0 || count <= 0 || count > (MaxPayload + ChunkSize - 1) / ChunkSize ||
                    index < 0 || index >= count || size <= 0 || size > ChunkSize ||
                    (index < count - 1 && size != ChunkSize) || reader.BaseStream.Length - reader.BaseStream.Position != size)
                    throw new IOException("Invalid campaign chunk");
                if (id > receivedRevision)
                {
                    receivedRevision = id;
                    partCount = count;
                    receivedCount = 0;
                    parts = new byte[count][];
                    receiveDeadline = Time.unscaledTime + 30f;
                }
                if (count != partCount) throw new IOException("Campaign chunk count changed");
                byte[] part = reader.ReadBytes(size);
                if (parts[index] != null) return;
                parts[index] = part;
                if (++receivedCount != count) return;
                using (var stream = new MemoryStream())
                {
                    foreach (byte[] item in parts) stream.Write(item, 0, item.Length);
                    parts = null;
                    if (stream.Length > MaxPayload) throw new IOException("Campaign too large");
                    var info = BattleInfo.FromBytes(stream.ToArray());
                    if (info.levelIndex != -1) throw new IOException("Campaign snapshot contains a battle");
                    SpectatorSession.Instance.ViewCampaign(info);
                    haveCampaign = true;
                    AppliedRevision = receivedRevision;
                }
            }
            catch (Exception e)
            {
                parts = null;
                ModLog.Problem("campaign_sync_receive", "Campaign sync receive failed: " + e);
            }
        }
    }
}
