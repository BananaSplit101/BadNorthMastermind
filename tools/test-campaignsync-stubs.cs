using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object o) { return o != null; } }
    public class MonoBehaviour : Object { }
    public class GameObject { public bool activeInHierarchy; }
    public static class Time { public static float unscaledTime; }
}
public class Singleton<T> where T : class { public static T instance; }
namespace BepInEx { public static class Paths { public static string BepInExRootPath = "."; } }
namespace HarmonyLib
{
    public class Traverse
    {
        public static object Value = new Voxels.TowerDefense.ProfileInternals.SaveGameHandlerStub();
        private object target;
        public static Traverse Create(Type type) { return new Traverse(); }
        public static Traverse Create(object target) { return new Traverse { target = target }; }
        public Traverse Field(string name) { return this; }
        public Traverse<T> Field<T>(string name) { return new Traverse<T>(target, name); }
        public object GetValue() { return Value; }
        public void SetValue(object value) { Value = value; }
    }
}
namespace HarmonyLib
{
    public class Traverse<T>
    {
        private readonly object target; private readonly string name;
        public Traverse(object target, string name) { this.target = target; this.name = name; }
        public T Value => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);
    }
}
namespace RTM.Utilities { public class WeakReference<T> where T : class { public T Target { get; set; } } }
namespace Steamworks
{
    public struct CSteamID : IEquatable<CSteamID>
    {
        public ulong m_SteamID;
        public CSteamID(ulong id) { m_SteamID = id; }
        public static CSteamID Nil = new CSteamID(0);
        public bool Equals(CSteamID other) { return m_SteamID == other.m_SteamID; }
        public override bool Equals(object other) { return other is CSteamID && Equals((CSteamID)other); }
        public override int GetHashCode() { return m_SteamID.GetHashCode(); }
        public static bool operator ==(CSteamID a, CSteamID b) { return a.Equals(b); }
        public static bool operator !=(CSteamID a, CSteamID b) { return !a.Equals(b); }
    }
}
namespace Voxels.TowerDefense.ProfileInternals
{
    public interface ISaveGameHandler { }
    public class SaveGameHandlerStub : ISaveGameHandler { }
    public class SaveGameUtilities { }
    [Serializable] public class UserSave { }
    public class CampaignSave
    {
        public int seed;
        public List<int> levelStates = new List<int> { 0, 1, 2 };
    }
    public class CampaignSaveMeta
    {
        public static implicit operator CampaignSaveMeta(CampaignSave save) { return new CampaignSaveMeta(); }
    }
}
namespace Voxels.TowerDefense
{
    using UnityEngine;
    using Voxels.TowerDefense.ProfileInternals;
    public static class Profile { public static CampaignSave campaign; public static CampaignSaveMeta activeCampaignMeta; public static UserSave userSave = new UserSave(); public static bool Loading; }
    public class State
    {
        public GameObject gameObject = new GameObject();
        public Action<bool> Changed;
        public void SetActive(bool newActive) { gameObject.activeInHierarchy = newActive; if (Changed != null) Changed(newActive); }
    }
    public class Stack : Object
    {
        public State stateLoading = new State(), stateLevel = new State(), stateCampaign = new State(), stateMeta = new State();
    }
    public class CampaignManager : Object
    {
        private RTM.Utilities.WeakReference<Voxels.TowerDefense.CampaignGeneration.Campaign> _campaign = new RTM.Utilities.WeakReference<Voxels.TowerDefense.CampaignGeneration.Campaign>();
        public Voxels.TowerDefense.CampaignGeneration.Campaign campaign { get { return _campaign.Target; } set { _campaign.Target = value; } }
        public IslandGenerator islandGenerator = new IslandGenerator();
        public void ClearCampaign()
        {
            if (Singleton<IslandGameplayManager>.instance.island != null) throw new Exception("Clear before native island leave!");
            // Like a native exit hook left unset by a campaign that never finished generating.
            if (BadNorthMP.Net.CampaignSyncTest.FailClear) throw new NullReferenceException("mock exit hook failure");
            BadNorthMP.Net.CampaignSyncTest.Flow.Add("clear"); campaign = null; Profile.campaign = null;
        }
        public static IEnumerator GenerateCampaign(CampaignSave save, bool campaignIsNew)
        {
            BadNorthMP.Net.CampaignSyncTest.Flow.Add("generate");
            if (BadNorthMP.Net.CampaignSyncTest.FailGenerate) throw new Exception("mock generation failure");
            Singleton<CampaignManager>.instance.campaign = new Voxels.TowerDefense.CampaignGeneration.Campaign(save);
            yield return null;
        }
    }
    public class IslandGenerator
    {
        public IEnumerator ShutDownRoutine()
        {
            if (Singleton<IslandGameplayManager>.instance.island != null) throw new Exception("Shutdown before native island leave!");
            if (BadNorthMP.Net.CampaignSyncTest.FailShutdown) throw new Exception("mock shutdown failure");
            BadNorthMP.Net.CampaignSyncTest.Flow.Add("shutdown"); yield return null;
        }
    }
    public class Island : Object
    {
        public enum State { Playing, Idle }
        public State state = State.Playing;
        public string name = "test";
        public Voxels.TowerDefense.CampaignGeneration.LevelNode levelNode;
        public Faction english = new Faction(), vikings = new Faction();
        public NavSpotter navSpotter = new NavSpotter();
        public Village village;
    }
    public class Faction { public List<int> agents = new List<int>(); }
    public class NavSpotter { public List<int> navSpots = new List<int>(); }
    public class Village : Object { public int[] houses; }
    public class LevelCamera : Object { }
    public class IslandGameplayManager : Object
    {
        public Island island;
        public IEnumerator LeaveIslandRoutine(bool reset)
        {
            if (reset) throw new Exception("Spectator must not restore cached defender profile stats");
            if (BadNorthMP.Net.CampaignSyncTest.FailLeave) throw new Exception("mock native leave failure");
            BadNorthMP.Net.CampaignSyncTest.Flow.Add("leave"); yield return null;
            island = null; BadNorthMP.Net.CampaignSyncTest.Flow.Add("leave-complete");
        }
    }
}
namespace Voxels.TowerDefense.CampaignGeneration
{
    using Voxels.TowerDefense;
    using Voxels.TowerDefense.ProfileInternals;
    public class Campaign : UnityEngine.Object
    {
        public List<LevelNode> levels = new List<LevelNode>();
        public Campaign(CampaignSave save) { for (int i = 0; i < 3; i++) levels.Add(new LevelNode { index = i }); }
        public void Destroy() { BadNorthMP.Net.CampaignSyncTest.Flow.Add("destroy"); }
    }
    public class IslandProxy
    {
        public enum State { None }
        public State state;
        public bool preparingToPlay;
        public void UpdateBuildPriority() { }
        public void GenerateIsland() { }
    }
    public class LevelNode
    {
        public int index;
        public IslandProxy islandProxy = new IslandProxy();
        public void Play()
        {
            if (BadNorthMP.Net.CampaignSyncTest.FailPlay) throw new Exception("mock PlayLevel failure");
            Singleton<IslandGameplayManager>.instance.island = new Island { levelNode = this };
            Singleton<Stack>.instance.stateCampaign.SetActive(false);
            Singleton<Stack>.instance.stateLevel.SetActive(true);
        }
    }
}
namespace Voxels.TowerDefense.UI
{
    public static class LoadingScreen
    {
        private static Action completion;
        private static IEnumerator routine;
        public static void BeginLoadingPhase(string description, Action completionCallback, params IEnumerator[] routines)
        {
            if (BadNorthMP.Net.CampaignSyncTest.FailLoadingStart) throw new Exception("mock loading setup failure");
            completion = completionCallback; routine = routines[0];
            Singleton<Voxels.TowerDefense.Stack>.instance.stateLoading.SetActive(true);
        }
        public static void Finish()
        {
            if (routine == null) return; // nothing started loading
            while (routine.MoveNext()) { }
            routine = null;
            Singleton<Voxels.TowerDefense.Stack>.instance.stateLoading.SetActive(false);
            var action = completion; completion = null; action();
        }
    }
}
namespace BadNorthMP
{
    public static class ModLog { public static int Errors; public static void Info(string s) { } public static void Warn(string s) { Errors++; } public static void Error(string s) { Errors++; } }
}
namespace BadNorthMP.UI
{
    public static class MultiplayerScreen { public static void CloseForBattle() { } }
    public static class MapNotice { public static string Last; public static void Show(string text, float duration) { Last = text; } }
}
namespace BadNorthMP.Spectator
{
    public static class PuppetWorld
    {
        public static int Buffered, PuppetCount, ShipCount;
        public static void Reset(bool keepBuffered = false) { if (!keepBuffered) Buffered = 0; }
    }
}
namespace BadNorthMP.Net
{
    using Steamworks;
    using Voxels.TowerDefense;
    using Voxels.TowerDefense.ProfileInternals;
    using BadNorthMP.Spectator;
    public enum MsgType : byte { HostLoading = 45, CampaignChunk = 46, CampaignClosed = 47, CampaignRequest = 68 }
    public static class PartyFlow { public static float StartedAt; }
    public static class Packet
    {
        public static byte[] Build(MsgType type, Action<BinaryWriter> body = null)
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            { writer.Write((byte)type); if (body != null) body(writer); return stream.ToArray(); }
        }
    }
    public static class Router
    {
        public delegate void Handler(CSteamID sender, BinaryReader r);
        public static Dictionary<MsgType, Handler> Handlers = new Dictionary<MsgType, Handler>();
        public static void On(MsgType t, Handler h) { Handlers[t] = h; }
        public static void Dispatch(CSteamID sender, byte[] p)
        {
            using (var reader = new BinaryReader(new MemoryStream(p))) { var t = (MsgType)reader.ReadByte(); Handlers[t](sender, reader); }
        }
    }
    public class SteamSession : UnityEngine.Object
    {
        public static SteamSession Instance = new SteamSession();
        public CSteamID Lobby = new CSteamID(10), Me = new CSteamID(2);
        public bool InLobby { get { return Lobby != CSteamID.Nil; } }
        public List<CSteamID> members = new List<CSteamID> { new CSteamID(1), new CSteamID(2) };
        public List<byte[]> Sent = new List<byte[]>();
        public List<CSteamID> Members() { return members; }
        public void Send(CSteamID member, byte[] p) { Sent.Add(p); }
        public void Broadcast(byte[] p) { }
        public event Action<CSteamID> ConnectionReset = delegate { };
        public void Reset(CSteamID member) { ConnectionReset(member); }
        public static string NameOf(CSteamID id) { return id.m_SteamID.ToString(); }
    }
    public static class Roles
    {
        public static bool IsDefender, IsMastermind = true, WatchesBattle = true;
        public static CSteamID DefenderId = new CSteamID(1);
    }
    public static class Updater { public static bool Compatible = true; public static bool IsCompatible(CSteamID member) { return true; } }
    public class MultiplayerGame : UnityEngine.Object { public static MultiplayerGame Instance = new MultiplayerGame(); public void AddEvent(string s) { } }
    public static class BattleStream { public const int FileMagic = 1, FileVersion = 6; public static bool Active; }
    public class BattleInfo
    {
        public int levelIndex;
        public byte[] campaignSave;
        public static int Captures;
        public static BattleInfo Capture(int level) { Captures++; return new BattleInfo { levelIndex = level, campaignSave = BitConverter.GetBytes(Profile.campaign.seed) }; }
        public CampaignSave LoadCampaignSave()
        {
            if (campaignSave.Length < 4) throw new IOException("corrupt campaign save");
            return new CampaignSave { seed = BitConverter.ToInt32(campaignSave, 0) };
        }
        public byte[] ToBytes()
        {
            using (var s = new MemoryStream()) using (var w = new BinaryWriter(s)) { w.Write(levelIndex); w.Write(campaignSave); return s.ToArray(); }
        }
        public static BattleInfo FromBytes(byte[] data)
        {
            using (var r = new BinaryReader(new MemoryStream(data))) return new BattleInfo { levelIndex = r.ReadInt32(), campaignSave = r.ReadBytes(data.Length - 4) };
        }
    }
    public static class CampaignSyncTest
    {
        public static List<string> Flow = new List<string>();
        public static bool FailPlay, FailGenerate, FailLoadingStart, FailLeave, FailShutdown, FailClear;
        static SpectatorSession watcher;
        static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
        static void Tick() { UnityEngine.Time.unscaledTime += 1; typeof(SpectatorSession).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(watcher, null); }
        static BattleInfo Info(int seed, int level = -1) { return new BattleInfo { levelIndex = level, campaignSave = BitConverter.GetBytes(seed) }; }
        static byte[] Part(int rev, int index, int count, byte[] bytes, int declared = -1)
        {
            return Packet.Build(MsgType.CampaignChunk, w => { w.Write(rev); w.Write(index); w.Write(count); w.Write(declared < 0 ? bytes.Length : declared); w.Write(bytes); });
        }
        static void Deliver(byte[] p) { Router.Dispatch(Roles.DefenderId, p); }
        static void Complete() { Tick(); Voxels.TowerDefense.UI.LoadingScreen.Finish(); }
        public static void Run()
        {
            SpectatorSession.CopyUserSave = save => new UserSave(); // no BinaryFormatter on this runtime
            var ownProgress = Profile.userSave;
            Singleton<Stack>.instance = new Stack();
            Singleton<CampaignManager>.instance = new CampaignManager();
            Singleton<IslandGameplayManager>.instance = new IslandGameplayManager();
            Singleton<Stack>.instance.stateCampaign.Changed = active => { if (active) Singleton<Stack>.instance.stateLevel.SetActive(false); };
            Singleton<Stack>.instance.stateMeta.Changed = active =>
            {
                if (!active) return;
                Singleton<Stack>.instance.stateCampaign.SetActive(false);
                Singleton<Stack>.instance.stateLevel.SetActive(false);
                Singleton<CampaignManager>.instance.ClearCampaign();
            };
            watcher = new SpectatorSession();
            typeof(SpectatorSession).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(watcher, null);
            CampaignSync.Init();
            object originalHandler = HarmonyLib.Traverse.Value;
            var originalMeta = new CampaignSaveMeta(); Profile.activeCampaignMeta = originalMeta;
            var originalCampaign = new CampaignSave { seed = 42 }; Profile.campaign = originalCampaign;
            // A watcher whose campaign hasn't arrived asks the leader again: 10 s after the party started, then every 10 s.
            Func<int> requests = () => SteamSession.Instance.Sent.FindAll(p => p[0] == (byte)MsgType.CampaignRequest).Count;
            UnityEngine.Time.unscaledTime = 1f; PartyFlow.StartedAt = 1f;
            UnityEngine.Time.unscaledTime = 6f; CampaignSync.Update(); Check(requests() == 0, "asked for the campaign too early");
            UnityEngine.Time.unscaledTime = 12f; CampaignSync.Update(); Check(requests() == 1, "missing campaign not requested");
            UnityEngine.Time.unscaledTime = 15f; CampaignSync.Update(); Check(requests() == 1, "campaign requested again too soon");
            UnityEngine.Time.unscaledTime = 23f; CampaignSync.Update(); Check(requests() == 2, "missing campaign not requested again");
            Router.Dispatch(new CSteamID(1), Packet.Build(MsgType.CampaignRequest)); // only a leader answers; a watcher ignores it
            SteamSession.Instance.Reset(new CSteamID(1));                          // likewise a reset: nothing to resend here
            Deliver(Part(1, 0, 1, Info(100).ToBytes()));
            Complete();
            Check(SpectatorSession.ViewingCampaign && Profile.campaign.seed == 100, "initial native map missing");
            UnityEngine.Time.unscaledTime += 30f; CampaignSync.Update();
            Check(requests() == 2, "campaign requested after it arrived");
            Check(!Object.ReferenceEquals(originalHandler, HarmonyLib.Traverse.Value), "save handler not protected");
            Deliver(Part(1, 0, 1, Info(999).ToBytes())); Tick();
            Check(Profile.campaign.seed == 100, "duplicate revision reapplied");
            var payload = new byte[49152 + 8]; Array.Copy(Info(200).ToBytes(), payload, 8);
            var first = new byte[49152]; var last = new byte[8]; Array.Copy(payload, first, first.Length); Array.Copy(payload, first.Length, last, 0, last.Length);
            Deliver(Part(2, 1, 2, last)); Deliver(Part(2, 1, 2, last)); Tick();
            Check(Profile.campaign.seed == 100, "partial/duplicate chunk completed transfer");
            Deliver(Part(2, 0, 2, first)); Complete();
            Check(Profile.campaign.seed == 200, "out-of-order bounded transfer failed");
            Deliver(Part(1, 0, 1, Info(999).ToBytes())); Tick(); Check(Profile.campaign.seed == 200, "old revision applied");
            Deliver(Part(3, 0, 1000000, new byte[1]));
            Deliver(Part(3, 0, 1, new byte[49153]));
            Deliver(Part(3, 0, 1, new byte[3], 20));
            Tick(); Check(Profile.campaign.seed == 200, "malformed payload replaced map");
            Router.Dispatch(new CSteamID(99), Part(4, 0, 1, Info(999).ToBytes())); Tick();
            Check(Profile.campaign.seed == 200, "unauthorized sender replaced map");
            Deliver(Part(3, 0, 2, first)); UnityEngine.Time.unscaledTime += 31; Tick();
            Deliver(Part(3, 1, 2, last)); Tick(); Check(Profile.campaign.seed == 200, "expired transfer revived");
            Deliver(Part(4, 0, 1, Info(300).ToBytes()));
            Tick(); // map transition has begun, now the next battle and its history arrive
            watcher.Watch(Info(400, 1)); PuppetWorld.Buffered = 7;
            Voxels.TowerDefense.UI.LoadingScreen.Finish();
            Check(PuppetWorld.Buffered == 7, "map completion discarded pending battle history");
            Complete(); Check(SpectatorSession.IslandReady() && PuppetWorld.Buffered == 7, "battle did not preserve reliable history");
            Flow.Clear(); watcher.BattleEnded(); watcher.Watch(Info(500, 2)); PuppetWorld.Buffered = 9;
            Complete();
            Check(string.Join(",", Flow.ToArray()) == "selection-clear,leave,leave-complete,shutdown,clear,generate", "native lifecycle order wrong");
            Check(SpectatorSession.IslandReady() && Profile.campaign.seed == 500 && PuppetWorld.Buffered == 9, "next live battle ignored");
            Deliver(Part(5, 0, 1, Info(600).ToBytes())); Tick();
            Check(Profile.campaign.seed == 500, "map update interrupted live battle");
            watcher.BattleEnded(); Complete(); Check(SpectatorSession.ViewingCampaign && Profile.campaign.seed == 600, "post-battle progress missing");
            watcher.Watch(Info(650, 1)); Complete();
            FailLeave = true; watcher.Watch(Info(660, 2)); Complete(); FailLeave = false;
            Check(SpectatorSession.Active && !Object.ReferenceEquals(originalHandler, HarmonyLib.Traverse.Value), "native leave failure released save handler with live island");
            Complete(); Check(!SpectatorSession.Active && Singleton<IslandGameplayManager>.instance.island == null, "native leave failure did not recover");
            watcher.ViewCampaign(Info(670)); Complete();
            FailShutdown = true; watcher.ViewCampaign(Info(680)); Complete(); FailShutdown = false;
            Check(SpectatorSession.Active, "shutdown failure released protection before generator teardown");
            Complete(); Check(!SpectatorSession.Active, "shutdown failure did not recover");
            FailPlay = true; watcher.Watch(Info(700, 1)); Complete(); FailPlay = false;
            Complete(); Check(!SpectatorSession.Active && Object.ReferenceEquals(originalHandler, HarmonyLib.Traverse.Value), "PlayLevel failure left save protection stuck");
            Check(Object.ReferenceEquals(originalMeta, Profile.activeCampaignMeta), "local save meta not restored");
            Check(Object.ReferenceEquals(originalCampaign, Profile.campaign), "local campaign not restored");
            watcher.ViewCampaign(new BattleInfo { levelIndex = -1, campaignSave = new byte[1] }); Tick();
            Check(!SpectatorSession.Active, "corrupt map entered spectator mode");
            FailLoadingStart = true; watcher.ViewCampaign(Info(750)); Tick(); FailLoadingStart = false;
            Complete(); Check(!SpectatorSession.Active && Object.ReferenceEquals(originalHandler, HarmonyLib.Traverse.Value), "loading setup failure stuck transitioning");
            FailGenerate = true; watcher.ViewCampaign(Info(800)); Complete(); FailGenerate = false;
            Complete();
            Check(!SpectatorSession.Active && Object.ReferenceEquals(originalHandler, HarmonyLib.Traverse.Value), "generation failure leaked handler");
            watcher.ViewCampaign(Info(900)); Complete();
            watcher.ViewCampaign(new BattleInfo { levelIndex = -1, campaignSave = new byte[1] }); Tick(); Complete();
            Check(!SpectatorSession.Active && Object.ReferenceEquals(originalHandler, HarmonyLib.Traverse.Value), "corrupt map during active session leaked protection");
            watcher.ViewCampaign(Info(900)); Complete();
            Deliver(Packet.Build(MsgType.CampaignClosed, w => w.Write(6))); Complete();
            Check(!SpectatorSession.Active && Singleton<IslandGameplayManager>.instance.island == null, "closed campaign did not exit");
            Deliver(Part(7, 0, 2, first)); Deliver(Part(8, 0, 1, Info(910).ToBytes())); Complete();
            Deliver(Part(7, 1, 2, last)); Tick(); Check(Profile.campaign.seed == 910, "superseded partial transfer applied");
            Deliver(Part(9, 0, 2, first)); SteamSession.Instance.Lobby = new CSteamID(11); Complete();
            Deliver(Part(9, 1, 2, last)); Tick(); Check(!SpectatorSession.Active, "old lobby partial transfer applied");
            // A player still at the title screen (profile not loaded yet) waits instead of generating the campaign.
            var profile = Profile.userSave; Profile.userSave = null; Flow.Clear(); BadNorthMP.UI.MapNotice.Last = null;
            watcher.ViewCampaign(Info(920)); Tick(); Tick();
            Check(!SpectatorSession.Active && !Flow.Contains("generate") && BadNorthMP.UI.MapNotice.Last != null, "campaign generated before the profile loaded");
            Profile.userSave = profile; Profile.Loading = true; Tick(); Check(!SpectatorSession.Active, "campaign generated while the profile was still loading");
            Profile.Loading = false; Complete();
            Check(SpectatorSession.ViewingCampaign && Profile.campaign.seed == 920, "campaign not opened once the profile loaded");
            // A campaign whose exit hooks throw (it never finished generating) is cleared anyway, so the next one loads.
            FailClear = true; Flow.Clear(); watcher.ViewCampaign(Info(930)); Complete(); FailClear = false;
            Check(Flow.Contains("destroy") && SpectatorSession.ViewingCampaign && Profile.campaign.seed == 930, "a campaign that wouldn't close blocked the next one");
            // Failing again and again stops after a few tries (not every frame), with the player's own saves back.
            FailGenerate = true; Flow.Clear(); BadNorthMP.UI.MapNotice.Last = null;
            for (int i = 0; i < 12; i++) { watcher.ViewCampaign(Info(940 + i)); Complete(); }
            FailGenerate = false;
            Check(Flow.FindAll(step => step == "generate").Count == 3, "repeated load failures kept retrying: " + Flow.FindAll(step => step == "generate").Count);
            Check(!SpectatorSession.Active && Object.ReferenceEquals(originalHandler, HarmonyLib.Traverse.Value) &&
                Object.ReferenceEquals(originalCampaign, Profile.campaign) && Object.ReferenceEquals(ownProgress, Profile.userSave), "giving up didn't restore the player's saves");
            Check(BadNorthMP.UI.MapNotice.Last != null && BadNorthMP.UI.MapNotice.Last.Contains("Restart"), "the player wasn't told to restart");
            watcher.ViewCampaign(Info(960)); Complete(); Check(!SpectatorSession.Active, "loading resumed after giving up");
            Roles.IsDefender = true; Roles.IsMastermind = Roles.WatchesBattle = false;
            SteamSession.Instance.Lobby = new CSteamID(20);
            Profile.campaign = new CampaignSave { seed = 1000 };
            Singleton<CampaignManager>.instance.campaign = new Voxels.TowerDefense.CampaignGeneration.Campaign(Profile.campaign);
            Singleton<Stack>.instance.stateCampaign.SetActive(true);
            SteamSession.Instance.Sent.Clear(); Tick();
            Check(SteamSession.Instance.Sent.Count == 1, "initial defender snapshot not sent");
            SteamSession.Instance.Sent.Clear(); Tick(); Check(SteamSession.Instance.Sent.Count == 0, "unchanged map resent");
            CampaignSync.MarkDirty(); Tick(); Check(SteamSession.Instance.Sent.Count == 1, "dirty save not synchronized");
            int previous; using (var r = new BinaryReader(new MemoryStream(SteamSession.Instance.Sent[0]))) { r.ReadByte(); previous = r.ReadInt32(); }
            CampaignSync.MarkDirty(); SteamSession.Instance.Sent.Clear(); Tick();
            using (var r = new BinaryReader(new MemoryStream(SteamSession.Instance.Sent[0]))) { r.ReadByte(); Check(r.ReadInt32() > previous, "revision failed to increment"); }
            SteamSession.Instance.members.Add(new CSteamID(3)); SteamSession.Instance.Sent.Clear(); Tick();
            Check(SteamSession.Instance.Sent.Count == 1, "late join snapshot not delivered");
            BattleStream.Active = true; CampaignSync.MarkDirty(); SteamSession.Instance.Sent.Clear(); Tick(); Check(SteamSession.Instance.Sent.Count == 0, "campaign replaced battle stream");
            BattleStream.Active = false; Tick(); Check(SteamSession.Instance.Sent.Count == 2, "deferred dirty map not sent after battle");
            // Broadcast upgrades: a quiet save refreshes the snapshot for late joiners only; a refused one resends to one player.
            SteamSession.Instance.Sent.Clear(); CampaignSync.MarkQuiet(); Tick(); Check(SteamSession.Instance.Sent.Count == 0, "quiet upgrade save reloaded current viewers");
            SteamSession.Instance.members.Add(new CSteamID(4)); SteamSession.Instance.Sent.Clear(); Tick(); Check(SteamSession.Instance.Sent.Count == 1, "late joiner missed the quietly refreshed campaign");
            CampaignSync.Resend(new CSteamID(3)); SteamSession.Instance.Sent.Clear(); Tick(); Check(SteamSession.Instance.Sent.Count == 1, "refused upgrade did not resend the campaign to its buyer");
            SteamSession.Instance.members.Remove(new CSteamID(4));
            Singleton<CampaignManager>.instance.campaign = null; Profile.campaign = null; SteamSession.Instance.Sent.Clear(); Tick();
            Check(SteamSession.Instance.Sent.Count == 2 && SteamSession.Instance.Sent[0][0] == 47, "campaign close not sent");
            SteamSession.Instance.Sent.Clear();
            byte[] large = new byte[100000];
            typeof(CampaignSync).GetMethod("Send", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { new CSteamID(3), large, 123 });
            Check(SteamSession.Instance.Sent.Count == 3, "sender did not split large payload");
            int total = 0;
            for (int i = 0; i < 3; i++)
                using (var r = new BinaryReader(new MemoryStream(SteamSession.Instance.Sent[i])))
                {
                    Check(r.ReadByte() == 46 && r.ReadInt32() == 123 && r.ReadInt32() == i && r.ReadInt32() == 3, "sender chunk header wrong");
                    int size = r.ReadInt32(); Check(size <= 49152 && r.BaseStream.Length - r.BaseStream.Position == size, "sender chunk not bounded"); total += size;
                }
            Check(total == large.Length, "sender lost payload bytes");
        }
    }
}
namespace BadNorthMP.Net {
 internal static class SelectionLifecycle { public static void Clear(){CampaignSyncTest.Flow.Add("selection-clear");} }
}

