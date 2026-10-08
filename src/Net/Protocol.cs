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
        Notice = 6,     // host -> all: string text (wave launched, request refused, ...)
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
