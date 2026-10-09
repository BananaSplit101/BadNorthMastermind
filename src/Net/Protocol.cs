using System.IO;

namespace BadNorthMP.Net
{
    internal enum MsgType : byte
    {
        Ping = 1,       // float sentAt
        Pong = 2,       // float sentAt (echoed)
        Chat = 3,       // string text
        Status = 4,     // host -> all: IslandStatus
        WaveRequest = 5,// mastermind -> host: (empty)
        Notice = 6,     // defender -> mastermind: string text (wave launched, request refused, ...)
        BattleChunk = 7,// defender -> mastermind: one piece of a BattleInfo (int transferId, int index, int count, bytes)
        BattleEnd = 8,  // defender -> mastermind: (empty) the defender left the island

        // Battle stream (defender -> mastermind, also recorded to disk). Handled by Spectator.PuppetWorld.
        SquadSpawn = 9,   // int squadId, byte side (0 English, 1 Viking), int heroIndex, Vector3 pos
        AgentSpawn = 10,  // int agentId, int squadId, string prefabName, bool isStandard, Vector3 pos
        AgentSpawned = 11,// int agentId: became visible/active (Agent.Spawn)
        AgentDied = 12,   // int agentId: started dying
        AgentGone = 13,   // int agentId, bool died: removed from the world
        Snapshot = 14,    // float streamTime, short count, then per agent: int id, Vector3 pos, short4 rot, byte health, int anim, ushort animTime, byte moving, ushort speed (x100)
        HouseState = 15,  // byte houseIndex, byte flags (HouseFlags), ushort fire (x1000)
        ShipSpawn = 16,   // int shipId, string prefabName
        ShipGone = 17,    // int shipId
        ShipSnapshot = 18,// float streamTime, short count, then per ship: int id, Vector3 pos, Quaternion rot, int anim, ushort animTime
        ProjectileSpawn = 19,    // int id, string prefabName (arrows, thrown axes, torches, bombs)
        ProjectileGone = 20,     // int id
        ProjectileSnapshot = 21, // float streamTime, short count, then per projectile: int id, Vector3 pos, short4 rot
        Sound = 22,       // byte target (SoundTarget), int targetId, Vector3 pos, bool byName, string name | int id, int action (-1 = default)
        Effect = 23,      // string effectName, Vector3 pos, short4 rot, Vector3 scale, bool onNavMesh
        Dust = 24,        // Vector3 pos, Vector3 dir: footstep/landing dust (DustParticles)
        LastBattleMessage = 39,

        // Mastermind game (not part of the recorded battle stream).
        BuildWave = 40,   // mastermind -> defender: int beach, byte count, then (int vikingType, int amount) pairs
        GiveUp = 41,      // mastermind -> defender: (empty)
        MasterState = 42, // defender -> mastermind: MasterState
        Result = 43,      // defender -> mastermind: bool ok, string message
    }

    internal static class SoundTarget
    {
        public const byte Position = 0, Agent = 1, Ship = 2, House = 3, Global = 4;
    }

    internal static class HouseFlags
    {
        public const byte Burning = 1, Destroying = 2, Pillaged = 4;
    }

    internal static class BinaryExtensions
    {
        public static void Write(this BinaryWriter w, UnityEngine.Vector3 v)
        {
            w.Write(v.x);
            w.Write(v.y);
            w.Write(v.z);
        }

        /// <summary>Unit quaternion as four shorts (8 bytes instead of 16).</summary>
        public static void WriteRotation(this BinaryWriter w, UnityEngine.Quaternion q)
        {
            w.Write((short)(q.x * 32767f));
            w.Write((short)(q.y * 32767f));
            w.Write((short)(q.z * 32767f));
            w.Write((short)(q.w * 32767f));
        }

        public static UnityEngine.Quaternion ReadRotation(this BinaryReader r)
        {
            var q = new UnityEngine.Quaternion(r.ReadInt16() / 32767f, r.ReadInt16() / 32767f, r.ReadInt16() / 32767f, r.ReadInt16() / 32767f);
            float length = UnityEngine.Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return length > 0.0001f ? new UnityEngine.Quaternion(q.x / length, q.y / length, q.z / length, q.w / length) : UnityEngine.Quaternion.identity;
        }

        public static UnityEngine.Vector3 ReadVector3(this BinaryReader r) =>
            new UnityEngine.Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    /// <summary>Snapshot of the host's battle, shown to the mastermind.</summary>
    internal struct IslandStatus
    {
        public bool onIsland;
        public int nextWave;     // 1-based index of the next wave to launch
        public int waveCount;
        public bool waveLaunching;
        public int squadsAlive;
        public int vikingsAlive;
        public bool mastermindControlsWaves;
        public float timeToNextWave; // seconds, only meaningful when the timer is running

        public void Write(BinaryWriter w)
        {
            w.Write(onIsland);
            w.Write(nextWave);
            w.Write(waveCount);
            w.Write(waveLaunching);
            w.Write(squadsAlive);
            w.Write(vikingsAlive);
            w.Write(mastermindControlsWaves);
            w.Write(timeToNextWave);
        }

        public static IslandStatus Read(BinaryReader r) => new IslandStatus
        {
            onIsland = r.ReadBoolean(),
            nextWave = r.ReadInt32(),
            waveCount = r.ReadInt32(),
            waveLaunching = r.ReadBoolean(),
            squadsAlive = r.ReadInt32(),
            vikingsAlive = r.ReadInt32(),
            mastermindControlsWaves = r.ReadBoolean(),
            timeToNextWave = r.ReadSingle(),
        };
    }

    internal static class Packet
    {
        public static byte[] Build(MsgType type, System.Action<BinaryWriter> body = null)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)type);
                body?.Invoke(writer);
                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
