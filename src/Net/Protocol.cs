using System.IO;

namespace BadNorthMP.Net
{
    internal enum MsgType : byte
    {
        Ping = 1,       // float sentAt
        Pong = 2,       // float sentAt (echoed)
        BattleChunk = 7,// defender -> mastermind: one piece of a BattleInfo (int transferId, int index, int count, bytes)
        BattleEnd = 8,  // defender -> mastermind: (empty) the defender left the island

        // Battle stream (campaign leader -> watchers). Handled by Spectator.PuppetWorld.
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
        UnitVisualNode = 25,
        UnitAnimatorFrame = 26,
        UnitNodeFrame = 27,
        UnitAgentFrame = 28,
        UnitVisualNodeGone = 29,
        CoinFrame = 30,   // pooled native coin pose and animator layers
        CoinGone = 31,    // int coinId
        HouseVisual = 32, // native house animations and healing progress display
        // Battle-stream messages must stay within SquadSpawn..LastBattleMessage (Router.IsBattleMessage).
        SquadTiles = 33,  // defender -> watchers: byte count, then per squad: int heroIndex, short navSpotIndex (its destination tile)
        SquadEvac = 34,   // defender -> watchers: byte count, then per squad: int heroIndex, bool canFlee
        SailIn = 36,          // leader -> watchers: byte phase (0 follow, 1 zoom out), int shipId, Vector3 shore: a commander's boat cinematic
        CommanderSquads = 35, // leader -> watchers: byte count, then (int commanderId, int squadId, byte trait, int flagAgentId): the mastermind's Viking commanders
        SquadAbilities = 37,  // leader -> watchers: byte squads, then per squad: int heroIndex, byte count, then per ability: int typeHash, byte flags, ushort cooldown (1/100 s), short charges
        HouseOccupants = 38,  // leader -> watchers: byte count, then per occupied house: byte houseIndex, int heroIndex, bool inside
        LastBattleMessage = 39,

        // Lobby, campaign and mastermind messages (handled by Router.On).
        WavePlan = 40,    // mastermind -> leader: byte landings, then per landing int tile, byte count, (int vikingType, int paid) pairs
        GiveUp = 41,      // mastermind -> defender: (empty)
        MasterState = 42, // defender -> mastermind: MasterState
        HostLoading = 45, // bool loading, bool ready: mastermind -> all
        CampaignChunk = 46,
        CampaignClosed = 47,
        BuyUpgrade = 48,
        PartyState = 49,
        PartyPick = 50,
        PartyBegin = 51,
        PartyOwnership = 52,
        PartyFailure = 53,
        PartyCatalog = 54,
        PartyDeleteSave = 71, // host -> campaign leader: int slot (0..4): delete that multiplayer campaign
        DefenderAction = 55,
        DefenderOrderResult = 56, // defender -> order sender: bool accepted, string message
        PartyClosed = 57,
        UpgradeRequest = 59, // defender -> leader: int token, int seed, int heroIndex, string upgrade, int level
        UpgradeResult = 60,  // leader -> requester: bool applied, string message
        UpgradeApplied = 61, // leader -> all: int token, int seed, ulong purchaser, int heroIndex, string upgrade, int level, int coinBank
        VoteCast = 62,       // defender -> leader: int token, int seed, int option (level index, or IslandVote.NextTurn)
        CommanderBuy = 64,    // mastermind -> leader: long session, int nextCommanderId, byte vikingType
        CommanderDeploy = 65, // mastermind -> leader: int commanderId, int tile
        PartyStateRequest = 67, // guest -> host: (empty) the party state again; sent after reconnecting
        BattleReady = 69,       // watcher -> leader: int levelIndex: my copy of the island is loaded
        LoadWait = 70,          // leader -> all: int levelIndex, byte count, ulong[] players still loading (0 = starting)
        MapWait = 72,           // leader -> all: int visit, int campaignRevision, byte count, ulong[] players still loading the map
        MapReady = 73,          // watcher -> leader: int visit, int campaignRevision: my campaign map has loaded
        NextTurnPlayed = 74,    // leader -> all: int token, int seed, int turnCount (after), float day: play this Next Turn on your own map
        CommanderSell = 75,     // mastermind -> leader: long session, int commanderId: sell it back for a share of its price
        DeployState = 76,       // leader -> all: PartyDeployment (who brings how many squads, everyone's picks, ready)
        DeployPick = 77,        // defender -> leader: int token, byte count, byte[] commanders, bool ready
        PlayerPing = 78,            // defender -> the other defenders: int levelIndex, byte kind, Vector3 position
        BattleRequest = 82,     // watcher -> leader: (empty) the running battle again; sent when a stuck load of it was abandoned
        CampaignRequest = 68,   // watcher -> leader: (empty) the current campaign again; sent while it hasn't arrived
        CommanderOrder = 66,  // mastermind -> leader: int commanderId, byte kind (0 move, 1 burn, 2 release), int target (tile or house)
        VoteState = 63,      // leader -> all: int token, int seed, float secondsLeft, float launchSecondsLeft, bool nextTurnAllowed, byte count, (ulong player, int option)*, int decided
        DefenderMove = 44, // int hero index, int navspot index
        Result = 43,      // defender -> mastermind: bool ok, string message

        // Error reports: anyone -> all: string incidentId, byte trigger, string summary. Everyone sends their log under
        // the code (Telemetry.Incidents). Read across versions (Router lets it past the version check): never change it.
        Incident = 90,
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
