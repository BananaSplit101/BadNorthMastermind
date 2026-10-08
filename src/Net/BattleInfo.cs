using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using Steamworks;
using Voxels.TowerDefense;
using Voxels.TowerDefense.ProfileInternals;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Everything the mastermind needs to rebuild the defender's island: the defender's whole campaign save
    /// (island generation depends on more than the seed) plus which level is being played.
    /// </summary>
    internal class BattleInfo
    {
        private const int FormatVersion = 1;

        public int levelIndex;
        public byte[] campaignSave;

        public static BattleInfo Capture(int levelIndex)
        {
            using (var stream = new MemoryStream())
            {
                // Same serializer the game uses for its own save files (Profile.SaveCampaign).
                new BinaryFormatter().Serialize(stream, Profile.campaign);
                return new BattleInfo { levelIndex = levelIndex, campaignSave = stream.ToArray() };
            }
        }

        public CampaignSave LoadCampaignSave()
        {
            using (var stream = new MemoryStream(campaignSave))
                return (CampaignSave)new BinaryFormatter().Deserialize(stream);
        }

        public byte[] ToBytes()
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(FormatVersion);
                w.Write(Plugin.Version);
                w.Write(levelIndex);
                w.Write(campaignSave.Length);
                w.Write(campaignSave);
                w.Flush();
                return stream.ToArray();
            }
        }

        public static BattleInfo FromBytes(byte[] data)
        {
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                int format = r.ReadInt32();
                string version = r.ReadString();
                if (format != FormatVersion)
                    throw new InvalidDataException($"Battle data from mod {version} isn't readable by {Plugin.Version}");
                var info = new BattleInfo { levelIndex = r.ReadInt32() };
                info.campaignSave = r.ReadBytes(r.ReadInt32());
                return info;
            }
        }
    }

    /// <summary>Splits large payloads into P2P-sized chunks and reassembles them.</summary>
    internal static class Chunker
    {
        private const int ChunkSize = 256 * 1024;

        private static int nextTransferId = 1;
        private static readonly Dictionary<int, byte[][]> pending = new Dictionary<int, byte[][]>();

        public static void Send(CSteamID to, byte[] payload)
        {
            int id = nextTransferId++;
            int count = Math.Max(1, (payload.Length + ChunkSize - 1) / ChunkSize);
            for (int i = 0; i < count; i++)
            {
                int offset = i * ChunkSize;
                int length = Math.Min(ChunkSize, payload.Length - offset);
                SteamSession.Instance.Send(to, Packet.Build(MsgType.BattleChunk, w =>
                {
                    w.Write(id);
                    w.Write(i);
                    w.Write(count);
                    w.Write(length);
                    w.Write(payload, offset, length);
                }));
            }
        }

        /// <summary>Returns the full payload once the last chunk of a transfer arrives, otherwise null.</summary>
        public static byte[] Receive(BinaryReader r)
        {
            int id = r.ReadInt32(), index = r.ReadInt32(), count = r.ReadInt32();
            byte[] part = r.ReadBytes(r.ReadInt32());

            if (!pending.TryGetValue(id, out byte[][] parts))
                pending[id] = parts = new byte[count][];
            parts[index] = part;
            foreach (byte[] p in parts)
            {
                if (p == null)
                    return null;
            }
            pending.Remove(id);

            using (var stream = new MemoryStream())
            {
                foreach (byte[] p in parts)
                    stream.Write(p, 0, p.Length);
                return stream.ToArray();
            }
        }
    }
}
