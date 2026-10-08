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
        Snapshot = 14,    // float streamTime, short count, then per agent: int id, Vector3 pos, short yaw, byte health, int anim, ushort animTime
        HouseState = 15,  // byte houseIndex, byte flags (HouseFlags)
        ShipSpawn = 16,   // int shipId, string prefabName
        ShipGone = 17,    // int shipId
        ShipSnapshot = 18,// float streamTime, short count, then per ship: int id, Vector3 pos, Quaternion rot, int anim, ushort animTime
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
