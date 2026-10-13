// Compile the actual purchase, economy, persistence, networking and effect code. Only the
// unavailable Unity/game/transport surface is stubbed; no upgrade logic is duplicated here.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        public static implicit operator bool(Object value) => value != null;
        public static bool operator !(Object value) => value == null;
    }
    public class GameObject : Object
    {
        public bool activeInHierarchy = true;
        private readonly Dictionary<Type, Component> components = new Dictionary<Type, Component>();
        public T AddComponent<T>() where T : Component
        { var result = (T)Activator.CreateInstance(typeof(T), true); result.gameObject = this; components[typeof(T)] = result; return result; }
        public T GetComponent<T>() where T : Component
        { components.TryGetValue(typeof(T), out var value); return (T)value; }
    }
    public class Component : Object
    {
        public GameObject gameObject = new GameObject();
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component { }
    public static class Time { public static float time, unscaledTime, deltaTime; }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Clamp(int n, int a, int b) => Math.Max(a, Math.Min(b, n));
        public static float Round(float f) => (float)Math.Round(f);
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute
    { public HarmonyPatch(Type type, string method) { } }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPriority : Attribute
    { public HarmonyPriority(int value) { } }
    public static class Priority { public const int Last = 0; }
}
namespace Steamworks
{
    public struct CSteamID : IEquatable<CSteamID>
    {
        public ulong m_SteamID;
        public CSteamID(ulong id) { m_SteamID = id; }
        public bool Equals(CSteamID b) => m_SteamID == b.m_SteamID;
        public override bool Equals(object b) => b is CSteamID && Equals((CSteamID)b);
        public override int GetHashCode() => m_SteamID.GetHashCode();
        public static bool operator ==(CSteamID a, CSteamID b) => a.Equals(b);
        public static bool operator !=(CSteamID a, CSteamID b) => !a.Equals(b);
    }
}
namespace BepInEx { public static class Paths { public static string ConfigPath; } }
namespace Voxels.TowerDefense
{
    public static class Singleton<T> { public static T instance; }
    public class AgentState { public bool active = true; public UnityEngine.GameObject gameObject = new UnityEngine.GameObject(); }
    public class Stack : UnityEngine.MonoBehaviour
    { public AgentState stateCampaign = new AgentState(), stateLoading = new AgentState(); }
    public class Faction : UnityEngine.MonoBehaviour
    {
        public enum Side { English, Viking }
        public Side side;
        public List<Squad> allSquads = new List<Squad>();
        public List<Agent> agents = new List<Agent>();
    }
    public class Squad : UnityEngine.MonoBehaviour { public List<Agent> agents = new List<Agent>(); }
    public class Agent : UnityEngine.MonoBehaviour
    {
        public Faction faction;
        public float health = 1f, maxHealth = 1f;
        public AgentState spawned = new AgentState();
        public void Spawn() { }
        public bool DealDamage(Attack attack) => false;
    }
    public class AgentComponent : UnityEngine.MonoBehaviour { public Agent agent; }
    public class VikingAgent : AgentComponent
    {
        public enum Type { Sword, SwordShield, Archer, Tank, Beserker, AxeThrower, TankArcher, Twohanded }
        public VikingReference vikingReference;
        public Type type => vikingReference.type;
    }
    public class VikingReference : UnityEngine.Object { public VikingAgent.Type type; public Agent agent = new Agent(); public int bounty = 1; }
    public struct AttackSettings { public float damage; }
    public struct Attack { public UnityEngine.MonoBehaviour monoAttacker; public float damage; }
    public class LevelState { public int totalBounty = 1000, minWaveSpacing = 21, maxWaveSpacing = 21, wavesCount = 4, bountyPerWave = 1; }
    public class LevelNode : UnityEngine.Object
    { public int index = 7; public LevelState levelState = new LevelState(); public List<VikingReference> enemies = new List<VikingReference>(); public bool unlocked = true; public bool IsUnlocked() => unlocked; }
    public class Campaign : UnityEngine.Object { public List<LevelNode> levels = new List<LevelNode>(); }
    public class CampaignManager : UnityEngine.MonoBehaviour { public Campaign campaign = new Campaign(); }
    public class CampaignSave { public int seed = 42; public bool gameOver; public bool HasEverSeen(VikingAgent.Type t) => false; }
    public class CampaignSaveMeta { public int saveSlot = 100, campaignNumber = 1; }
    public static class Profile { public static CampaignSave campaign; public static CampaignSaveMeta activeCampaignMeta; }
    public class House { }
    public class EndOfLevel { public enum Reason { None, Won, Wiped, Fled } }
    public class Village : UnityEngine.Object { public event Action<House> onHouseDestroyed; public void Burn() => onHouseDestroyed?.Invoke(new House()); }
    public class Raid : UnityEngine.Object { public void StopAllCoroutines() { } }
    public class Island : UnityEngine.Object
    {
        public enum State { Playing }
        public State state;
        public LevelNode levelNode;
        public Village village = new Village();
        public Faction vikings = new Faction { side = Faction.Side.Viking };
        public Raid raid = new Raid();
    }
    public class IslandGameplayManager : UnityEngine.MonoBehaviour
    { public Island island; public States states = new States(); public class States { public AgentState running = new AgentState(), EndOfLevel = new AgentState { active = false }; } }
}
namespace Voxels.TowerDefense.Ballistics
{
    public class Shootable : UnityEngine.MonoBehaviour { public void Shoot() { } }
}
namespace BadNorthMP
{
    public static class ModLog { public static void Info(string s) { } public static void Error(string s) { } public static void Warn(string s) { } public static void Problem(string category, string s) { } }
    public static class GameAccess { public static Voxels.TowerDefense.Island CurrentIsland; }
}
namespace BadNorthMP.Spectator { public static class SpectatorSession { public static bool Active; } }
public static class FabricWrapper { public static string last; public static void PostEvent(string s) { last = s; } }
namespace BadNorthMP.UI { }
namespace BadNorthMP.Game
{
    public static class MultiplayerCampaigns { public static bool IsMultiplayer(Voxels.TowerDefense.CampaignSaveMeta m) => m != null && m.saveSlot >= 100; }
    public class BeachChoice { public int tileIndex = 10; public string label = "west"; }
    // Traits (flags, stat effects) need Unity; recruiting just rolls one. A fixed roll keeps the tests repeatable.
    internal enum VikingTrait : byte { None, Giant, Heavy, Iron, Medic, Sharp, Speed, Sturdy, Thorns, Short, Sick, Slash }
    internal static class VikingTraits
    {
        public static VikingTrait Random() => VikingTrait.Iron;
        public static string Name(int trait) => trait > 0 ? ((VikingTrait)trait).ToString() : "";
    }
    // Commanders sailing in needs the real island.
    internal static class SailIn { public static bool Arriving; }
    // Landing and ordering commanders needs the real island; the purchase and roster code above is real.
    public static class CommanderBattle
    {
        public static bool TryDeploy(int id, int tile, out string message) { message = "battle stub"; return false; }
        public static bool TryOrder(int id, byte kind, int target, out string message) { message = "battle stub"; return false; }
    }
    public static class BeachChoices { public static List<BeachChoice> Get(Voxels.TowerDefense.Island i) => new List<BeachChoice> { new BeachChoice(), new BeachChoice { tileIndex = 11, label = "east" } }; }
    public static class CustomWaves
    {
        public static bool MastermindMode => Net.Roles.IsDefender && Net.Roles.HasMastermind && !Spectator.SpectatorSession.Active;
        public static int WavesSent;
        public static bool Ready = true, Fail;
        public static int SentCount;
        public static bool CanLaunch(out string error) { error = "not ready"; return MastermindMode && Economy.Active && Ready; }
        public static void ReplaceRaid(Voxels.TowerDefense.Raid raid) { }
        public static bool TryLaunch(Voxels.TowerDefense.Island island, int beach,
            List<KeyValuePair<Voxels.TowerDefense.VikingReference, int>> units, out string error, out int unitCount)
        {
            unitCount = 0; foreach (var u in units) unitCount += u.Value;
            error = "ship capacity/placement failure";
            if (Fail || beach < 0 || unitCount > 16) return false;
            SentCount = unitCount; WavesSent++; return true;
        }
    }
}
namespace BadNorthMP.Net
{
    internal enum MsgType : byte { WavePlan = 40, GiveUp = 41, MasterState = 42, Result = 43, BuyUpgrade = 48, CommanderBuy = 64, CommanderDeploy = 65, CommanderOrder = 66, CommanderSell = 75 }
    public static class Roles
    {
        public static bool IsDefender = true, IsMastermind, HasMastermind = true;
        public static Steamworks.CSteamID MastermindId = new Steamworks.CSteamID(1), DefenderId = new Steamworks.CSteamID(2);
    }
    public class SteamSession
    {
        public static SteamSession Instance = new SteamSession();
        public Steamworks.CSteamID Me = new Steamworks.CSteamID(2);
        public readonly List<byte[]> sent = new List<byte[]>();
        public void Send(Steamworks.CSteamID id, byte[] bytes, bool reliable = true) => sent.Add(bytes);
        public static string NameOf(Steamworks.CSteamID id) => "test";
    }
    internal static class Packet
    {
        public static byte[] Build(MsgType type, Action<BinaryWriter> body = null)
        { using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream)) { writer.Write((byte)type); body?.Invoke(writer); writer.Flush(); return stream.ToArray(); } }
    }
    internal static class Router
    {
        public delegate void Handler(Steamworks.CSteamID sender, BinaryReader reader);
        public static Dictionary<MsgType, Handler> handlers = new Dictionary<MsgType, Handler>();
        public static void On(MsgType type, Handler action) => handlers[type] = action;
        public static void Dispatch(Steamworks.CSteamID sender, byte[] data)
        { using (var reader = new BinaryReader(new MemoryStream(data))) { var type = (MsgType)reader.ReadByte(); handlers[type](sender, reader); } }
    }
    public class MultiplayerGame
    { public static MultiplayerGame Instance = new MultiplayerGame(); public static string last; public void Announce(string text) { last = text; } }
}
namespace BadNorthMP.Game
{
    public static class MastermindUpgradeTests
    {
        private static string directory;
        private static Voxels.TowerDefense.Island island;
        private static int assertions;
        private static void Assert(bool pass, string label)
        { if (!pass) throw new Exception(label); assertions++; }
        private static void Equal(float actual, float expected, string label) => Assert(Math.Abs(actual - expected) < 0.001f, label + ": " + actual + " != " + expected);
        private static void Fixture(bool battle)
        {
            Economy.End();
            MastermindSession.Reset();
            Net.Roles.IsDefender = Net.Roles.HasMastermind = true;
            Net.Roles.IsMastermind = false;
            Spectator.SpectatorSession.Active = false;
            UnityEngine.Time.time = UnityEngine.Time.unscaledTime = 0f;
            BepInEx.Paths.ConfigPath = directory;
            Voxels.TowerDefense.Profile.campaign = new Voxels.TowerDefense.CampaignSave();
            Voxels.TowerDefense.Profile.activeCampaignMeta = new Voxels.TowerDefense.CampaignSaveMeta { campaignNumber = assertions + 1 };
            island = new Voxels.TowerDefense.Island { levelNode = new Voxels.TowerDefense.LevelNode() };
            for (int i = 0; i < 8; i++) island.levelNode.enemies.Add(new Voxels.TowerDefense.VikingReference { type = (Voxels.TowerDefense.VikingAgent.Type)i });
            var campaign = new Voxels.TowerDefense.CampaignManager(); campaign.campaign.levels.Add(island.levelNode);
            Voxels.TowerDefense.Singleton<Voxels.TowerDefense.CampaignManager>.instance = campaign;
            var stack = new Voxels.TowerDefense.Stack(); stack.stateLoading.gameObject.activeInHierarchy = false;
            Voxels.TowerDefense.Singleton<Voxels.TowerDefense.Stack>.instance = stack;
            Voxels.TowerDefense.Singleton<Voxels.TowerDefense.IslandGameplayManager>.instance = new Voxels.TowerDefense.IslandGameplayManager { island = island };
            GameAccess.CurrentIsland = battle ? island : null;
            CustomWaves.Ready = true; CustomWaves.Fail = false; CustomWaves.SentCount = 0;
            island.levelNode.levelState.bountyPerWave = 100;
            MastermindUpgradeSave.EnsureCampaign(); MastermindSession.SetWarChest(1000f);
            if (battle) Economy.Begin(island);
            Net.SteamSession.Instance.sent.Clear(); Net.MastermindNet.Init();
        }
        private static bool Buy(Voxels.TowerDefense.VikingAgent.Type type, UnitUpgrade kind, int? revision = null)
            => MastermindUpgrades.TryBuy(MastermindSession.UpgradeSession, revision ?? MastermindSession.UpgradeRevision,
                Economy.Active ? island.levelNode.index : -1, type, kind, out string message);
        private static byte[] Request(Voxels.TowerDefense.VikingAgent.Type type, UnitUpgrade kind, int revision)
            => Net.Packet.Build(Net.MsgType.BuyUpgrade, w => { w.Write(MastermindSession.UpgradeSession); w.Write(revision); w.Write(Economy.Active ? island.levelNode.index : -1); w.Write((byte)type); w.Write((byte)kind); });
        // Waves only sail on their timer: plan one, then let the timer run out.
        private static void Wave(int paid, params int[] types) { Plan(paid, types); Sail(); }
        private static void Sail() => Economy.Tick(Economy.SecondsToDeparture + 0.01f);
        private static void Plan(int paid, params int[] types) => Landings(new[] { 10 }, paid, types);
        /// <summary>The same Vikings at each of the given beach tiles.</summary>
        private static void Landings(int[] tiles, int paid, params int[] types)
        {
            Net.Router.Dispatch(Net.Roles.MastermindId, Net.Packet.Build(Net.MsgType.WavePlan, w =>
            {
                w.Write((byte)tiles.Length);
                foreach (int tile in tiles)
                {
                    w.Write(tile); w.Write((byte)types.Length);
                    foreach (int t in types) { w.Write(t); w.Write(paid); }
                }
            }));
        }
        private static float Chest => MastermindSession.WarChest;
        public static void Run()
        {
            directory = Path.Combine(Path.GetTempPath(), "bnmp-upgrade-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                Fixture(true);
                float restartBudget = Economy.WaveBudget;
                Assert(Buy(Voxels.TowerDefense.VikingAgent.Type.Sword, UnitUpgrade.Health), "upgrade before retry");
                Economy.Reward(50, "discarded attempt");
                Wave(4, 0);
                Economy.GiveUp();
                Economy.Restart();
                Assert(!Economy.Active && !Economy.GaveUp, "retry clears active and give-up state");
                Equal(Chest, 1000f, "retry restores the war chest without reward farming");
                Assert(MastermindSession.UpgradeLevel(Voxels.TowerDefense.VikingAgent.Type.Sword, UnitUpgrade.Health)==0,"retry rolls back battle purchases with refunded war chest");
                Economy.Begin(island);
                Equal(Economy.WaveBudget, restartBudget, "retry has the original wave budget");
                Assert(Economy.WaveIndex == 0, "retry starts from the first wave");
                Assert(!Economy.Done, "retry begins a fresh fight");
                Economy.GiveUp();Assert(Economy.Done,"retry can finish after mastermind gives up");
                Fixture(true);
                var sword = Voxels.TowerDefense.VikingAgent.Type.Sword;
                float before = Chest, budget = Economy.WaveBudget;
                Assert(Buy(sword, UnitUpgrade.Health), "first authoritative purchase");
                Equal(Chest, before - 8f, "battle upgrade paid from the war chest");
                Equal(Economy.WaveBudget, budget, "battle upgrade doesn't touch the wave budget");
                Assert(!Buy(sword, UnitUpgrade.Health, 0), "duplicate revision rejected");
                Equal(Chest, before - 8f, "duplicate spends no gold");
                Assert(!Buy((Voxels.TowerDefense.VikingAgent.Type)255, UnitUpgrade.Health), "invalid type rejected");
                Assert(!Buy(sword, (UnitUpgrade)255), "invalid kind rejected");
                CustomWaves.Ready = false; Assert(!Buy(sword, UnitUpgrade.Damage), "paused purchase rejected"); CustomWaves.Ready = true;
                Assert(!MastermindUpgrades.TryBuy(MastermindSession.UpgradeSession - 1, 1, 7, sword, UnitUpgrade.Damage, out _), "stale session rejected");
                Assert(!MastermindUpgrades.TryBuy(MastermindSession.UpgradeSession, 1, 8, sword, UnitUpgrade.Damage, out _), "wrong island rejected");
                Assert(Buy(sword, UnitUpgrade.Health) && Buy(sword, UnitUpgrade.Health), "all health tiers purchasable");
                before = Chest;
                Assert(!Buy(sword, UnitUpgrade.Health), "max level rejected"); Equal(Chest, before, "max level spends no gold");
                var shield = island.levelNode.enemies[1]; island.levelNode.enemies.Remove(shield);
                Assert(!Buy(shield.type, UnitUpgrade.Damage), "unavailable type rejected");
                // A type unlocked elsewhere in the campaign can be upgraded mid-fight too (the war chest's locks agree on map and island).
                var elsewhere = new Voxels.TowerDefense.LevelNode(); elsewhere.enemies.Add(shield);
                Voxels.TowerDefense.Singleton<Voxels.TowerDefense.CampaignManager>.instance.campaign.levels.Add(elsewhere);
                Assert(Buy(shield.type, UnitUpgrade.Damage), "a type unlocked on another island is upgradable in a fight");
                elsewhere.unlocked = false;
                Assert(!Buy(shield.type, UnitUpgrade.Health), "a type only on a locked island stays locked in a fight");
                Net.Roles.IsDefender = false; Assert(!Buy(sword, UnitUpgrade.Damage), "non-authority rejected"); Net.Roles.IsDefender = true;
                before = Chest;
                Net.Router.Dispatch(new Steamworks.CSteamID(999), Request(sword, UnitUpgrade.Damage, MastermindSession.UpgradeRevision));
                Equal(Chest, before, "foreign sender rejected");
                byte[] request = Request(sword, UnitUpgrade.Damage, MastermindSession.UpgradeRevision);
                Net.Router.Dispatch(Net.Roles.MastermindId, request); Net.Router.Dispatch(Net.Roles.MastermindId, request);
                Equal(Chest, before - 8, "network duplicate spends once");
                before = Chest;
                Array.Resize(ref request, request.Length + 1); Net.Router.Dispatch(Net.Roles.MastermindId, request);
                Equal(Chest, before, "trailing packet bytes rejected");
                Net.Router.Dispatch(Net.Roles.MastermindId, new byte[] { 48, 1 });
                Equal(Chest, before, "truncated packet rejected");
                Economy.GiveUp(); Assert(!Buy(sword, UnitUpgrade.Damage), "give-up purchase rejected");

                Fixture(true);
                foreach (var type in island.levelNode.enemies)
                {
                    var agent = new Voxels.TowerDefense.Agent { faction = island.vikings, maxHealth = 2f, health = 1f };
                    var viking = agent.gameObject.AddComponent<Voxels.TowerDefense.VikingAgent>(); viking.vikingReference = type;
                    var squad = new Voxels.TowerDefense.Squad(); squad.agents.Add(agent); island.vikings.allSquads.Add(squad);
                    Assert(Buy(type.type, UnitUpgrade.Health), "health available for " + type.type);
                    Equal(agent.maxHealth, 2.5f, "purchase updates existing live units for " + type.type);
                    Assert(Buy(type.type, UnitUpgrade.Damage), "damage available for " + type.type);
                    MastermindUpgrades.ApplyHealth(agent); Equal(agent.maxHealth, 2.5f, "native difficulty health multiplied");
                    Equal(agent.health, 1.25f, "wounds preserved"); MastermindUpgrades.ApplyHealth(agent);
                    Equal(agent.maxHealth, 2.5f, "health multiplier idempotent");
                    var attacker = new Voxels.TowerDefense.AgentComponent { agent = agent };
                    object[] args = { new Voxels.TowerDefense.Attack { monoAttacker = attacker, damage = 2f } };
                    typeof(MastermindUpgradeDamagePatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
                    Equal(((Voxels.TowerDefense.Attack)args[0]).damage, 2.5f, "melee damage upgraded for " + type.type);
                    args = new object[] { agent, new Voxels.TowerDefense.AttackSettings { damage = 2f } };
                    typeof(MastermindUpgradeProjectilePatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
                    Equal(((Voxels.TowerDefense.AttackSettings)args[1]).damage, 2.5f, "projectile damage upgraded for " + type.type);
                }
                object[] projectileArgs = { new Voxels.TowerDefense.Attack { monoAttacker = new Voxels.TowerDefense.Ballistics.Shootable(), damage = 2.5f } };
                typeof(MastermindUpgradeDamagePatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, projectileArgs);
                Equal(((Voxels.TowerDefense.Attack)projectileArgs[0]).damage, 2.5f, "projectile impact not doubled");
                var english = new Voxels.TowerDefense.Agent { faction = new Voxels.TowerDefense.Faction { side = Voxels.TowerDefense.Faction.Side.English } };
                Equal(MastermindUpgrades.DamageMultiplier(english), 1f, "English units unchanged");

                Fixture(true);
                for (int i = 0; i < 3; i++) Assert(Buy(sword, UnitUpgrade.Count), "count tier purchased");
                for (int level = 0; level <= 3; level++)
                    for (int paid = 0; paid <= 99; paid++) Assert(MastermindUpgrades.CrewCount(paid, level) == paid + paid * level / 4, "count rounding");
                // The timer starts with the game's first-wave call, then runs until the end screen even if the game's
                // running state drops out mid-fight (states above it switch while defenders play).
                var states = Voxels.TowerDefense.Singleton<Voxels.TowerDefense.IslandGameplayManager>.instance.states;
                Economy.Begin(island); states.running.active = false;
                float opening = Economy.SecondsToDeparture; Economy.Tick(3f);
                Assert(Economy.SecondsToDeparture == opening && !Economy.FightOn, "the timer waits for the first-wave call");
                states.running.active = true; Economy.Tick(1f); states.running.active = false; Economy.Tick(1f);
                Assert(Math.Abs(Economy.SecondsToDeparture - (opening - 2f)) < 0.01f && Economy.FightOn, "the timer runs on when the running state drops out");
                states.EndOfLevel.active = true; Economy.Tick(1f);
                Assert(Math.Abs(Economy.SecondsToDeparture - (opening - 2f)) < 0.01f && !Economy.FightOn, "the end screen stops the timer");
                states.running.active = true; states.EndOfLevel.active = false;
                // A retry: the island stays "playing" while it reloads. The waves wait for the reloaded island (whose
                // running state is off until the party lands), not start again on the old one with its fight still on.
                Economy.Tick(1f); Economy.Restart();
                Net.MastermindNet.Update(); Economy.Tick(30f);
                Assert(!Economy.Active && !Economy.FightOn && CustomWaves.SentCount == 0, "a retry doesn't restart the waves on the island it's leaving");
                states.running.active = false; Economy.Begin(island);
                Net.MastermindNet.Update(); Economy.Tick(30f);
                Assert(Economy.Active && !Economy.FightOn && Economy.WaveIndex == 0 && CustomWaves.SentCount == 0, "after a retry the timer waits for the party to land");
                states.running.active = true; Economy.Tick(1f);
                Assert(Economy.FightOn && Math.Abs(Economy.SecondsToDeparture - (opening - 1f)) < 0.01f, "the retried fight's timer starts from the top");
                Plan(4, 0);
                Assert(Economy.Plan.Count == 1 && Economy.WaveIndex == 0, "a plan never sails early");
                Sail();
                Assert(CustomWaves.SentCount == 7 && Economy.WaveIndex == 1 && Economy.Plan.Count == 0, "the planned wave sails on the timer with bonus crew");
                Equal(Economy.WaveBudget, 100f + 96f * EconomyConfig.CarryToNextWave, "bonus Vikings are free; half the rest carries");
                float waveBudget = Economy.WaveBudget;
                // Bad plans are refused whole, and nothing of them is kept.
                Plan(4, 0, 0); Assert(Economy.Plan.Count == 0, "duplicate wave types rejected");
                Plan(-1, 0); Assert(Economy.Plan.Count == 0, "negative wave count rejected");
                Plan(200, 0); Assert(Economy.Plan.Count == 0, "plan over the wave budget rejected");
                Landings(new[] { 10, 11 }, 100, 0); Assert(Economy.Plan.Count == 0, "landings together over the budget rejected");
                Landings(new[] { 10, 10 }, 2, 0); Assert(Economy.Plan.Count == 0, "the same beach twice rejected");
                Landings(new[] { 99 }, 2, 0); Assert(Economy.Plan.Count == 0, "unknown beach rejected");
                Landings(new[] { 10, 11, 10, 11, 10 }, 1, 0); Assert(Economy.Plan.Count == 0, "more landings than a wave allows rejected");
                Equal(Economy.WaveBudget, waveBudget, "rejected plans spend nothing");
                Assert(Economy.WaveIndex == 1, "rejected plans sail nothing");
                // One wave split over two beaches: both land when the timer runs out, and both are paid for.
                CustomWaves.WavesSent = 0;
                Landings(new[] { 10, 11 }, 3, 0);
                Assert(Economy.Plan.Count == 2 && Economy.WaveIndex == 1 && System.Math.Abs(Economy.PlanCost - 6f) < 0.001f, "a wave can land on two beaches");
                Sail();
                Assert(Economy.WaveIndex == 2 && CustomWaves.WavesSent == 2 && CustomWaves.SentCount == 5 && Economy.Plan.Count == 0, "both landings sail on the timer");
                Equal(Economy.WaveBudget, 100f + (waveBudget - 6f) * EconomyConfig.CarryToNextWave, "both landings are paid for");
                // The timer keeps running while the defenders' commanders sail in; a wave due then waits for them to land.
                SailIn.Arriving = true;
                float due = Economy.SecondsToDeparture;
                Economy.Tick(due * 0.5f);
                Assert(Math.Abs(Economy.SecondsToDeparture - due * 0.5f) < 0.01f, "the wave timer counts down while boats land");
                Economy.Tick(due);
                Assert(Economy.WaveIndex == 2 && Economy.SecondsToDeparture == 0f && Economy.HeldForLanding, "a due wave waits for the boats to land");
                SailIn.Arriving = false;
                Assert(!Economy.HeldForLanding, "nothing held once they've landed");
                // No plan: a random wave sails, shrunk until its ships fit.
                CustomWaves.SentCount = 0; Economy.Tick(Economy.SecondsToDeparture + 0.01f);
                Assert(Economy.WaveIndex == 3 && CustomWaves.SentCount > 0 && CustomWaves.SentCount <= 16, "random wave sails without a plan");
                // Waiting longer never builds a bigger wave than the schedule allows.
                Assert(Economy.WaveBudget < 3 * 100f, "budget can't pile up across waves");
                Economy.Tick(Economy.SecondsToDeparture + 0.01f);
                Assert(Economy.Done && FabricWrapper.last == "Mus/LastWave" && Net.MultiplayerGame.last == "Final wave!", "last wave sails with the native final-wave cue");
                Wave(1, 0); Assert(Economy.WaveIndex == 4, "no waves after the last one");
                // Rewards and the end-of-island payout go to the war chest.
                before = Chest; Economy.Reward(5, "house burned"); Equal(Chest, before + 5f, "battle rewards fill the war chest");
                before = Chest; Economy.Result = Voxels.TowerDefense.EndOfLevel.Reason.Won; Economy.End();
                Equal(Chest, before + EconomyConfig.IslandPayoutBase + EconomyConfig.IslandPayoutPerBounty * 1000f, "island payout");
                Fixture(true); before = Chest; Economy.Result = Voxels.TowerDefense.EndOfLevel.Reason.Wiped; Economy.End();
                Equal(Chest, before + EconomyConfig.IslandPayoutBase + (EconomyConfig.IslandPayoutPerBounty + EconomyConfig.WinBonusPerBounty) * 1000f, "win bonus when the defenders are wiped");
                Fixture(true); before = Chest; Economy.End(); Equal(Chest, before, "leaving mid-battle pays nothing");

                Fixture(false);
                Assert(Buy(sword, UnitUpgrade.Health), "map purchase allowed"); Equal(Chest, 992f, "map purchase debits the war chest");
                var stackForMap = Voxels.TowerDefense.Singleton<Voxels.TowerDefense.Stack>.instance;
                stackForMap.stateLoading.gameObject.activeInHierarchy = true;
                Assert(!Buy(sword, UnitUpgrade.Damage), "map purchase blocked while loading");
                stackForMap.stateLoading.gameObject.activeInHierarchy = false;
                long oldSession = MastermindSession.UpgradeSession;
                MastermindSession.Reset(); MastermindUpgradeSave.EnsureCampaign();
                Assert(MastermindSession.UpgradeLevel(sword, UnitUpgrade.Health) == 1, "sidecar reload restores upgrades");
                Equal(Chest, 992f, "sidecar reload restores the war chest");
                Assert(MastermindSession.UpgradeSession != oldSession, "reload rotates session token");
                Voxels.TowerDefense.Profile.activeCampaignMeta.campaignNumber++;
                MastermindUpgradeSave.EnsureCampaign();
                Assert(MastermindSession.UpgradeLevel(sword, UnitUpgrade.Health) == 0, "reused slot and seed separated by campaign identity");
                MastermindSession.SetWarChest(2f); Assert(!Buy(sword, UnitUpgrade.Health), "insufficient map gold rejected");
                Equal(Chest, 2f, "insufficient gold unchanged");
                island.levelNode.unlocked = false;
                Assert(MastermindUpgrades.AvailableTypes().Count == 0, "locked map types unavailable");

                string blocked = Path.Combine(directory, "blocked"); File.WriteAllText(blocked, "not a directory");
                BepInEx.Paths.ConfigPath = blocked; MastermindUpgradeSave.Reset(); MastermindUpgradeSave.EnsureCampaign();
                island.levelNode.unlocked = true; MastermindSession.SetWarChest(1000f);
                Assert(!Buy(sword, UnitUpgrade.Health), "disk failure rejects map purchase");
                Equal(Chest, 1000f, "map disk failure refunds gold");
                Assert(MastermindSession.UpgradeRevision == 0 && MastermindSession.UpgradeLevel(sword, UnitUpgrade.Health) == 0, "disk failure rolls back levels and revision");
                GameAccess.CurrentIsland = island; Economy.Begin(island); before = Chest;
                Assert(!Buy(sword, UnitUpgrade.Health), "disk failure rejects battle purchase"); Equal(Chest, before, "battle disk failure refunds gold");

                // Viking commanders: recruited from the war chest, saved with the campaign, rest after an island.
                Fixture(false);
                Func<int, int, byte[]> recruit = (type, next) => Net.Packet.Build(Net.MsgType.CommanderBuy, w =>
                { w.Write(MastermindSession.UpgradeSession); w.Write(next); w.Write((byte)type); });
                before = Chest;
                Net.Router.Dispatch(Net.Roles.MastermindId, recruit(0, MastermindSession.NextCommanderId));
                Assert(MastermindSession.Commanders.Count == 1 && MastermindSession.Commanders[0].type == 0 && MastermindSession.Commanders[0].id == 1, "commander recruited on the map");
                Assert(MastermindSession.Commanders[0].trait == (int)VikingTrait.Iron, "a recruited commander rolls a trait");
                Equal(Chest, before - CommanderRoster.Price(1, 0), "commander paid from the war chest");
                Net.Router.Dispatch(Net.Roles.MastermindId, recruit(0, 1));
                Assert(MastermindSession.Commanders.Count == 1, "duplicate recruit request rejected");
                island.levelNode.unlocked = false;
                Net.Router.Dispatch(Net.Roles.MastermindId, recruit(3, MastermindSession.NextCommanderId));
                Assert(MastermindSession.Commanders.Count == 1, "locked type can't be recruited"); island.levelNode.unlocked = true;
                for (int i = 0; i < 3; i++) Net.Router.Dispatch(Net.Roles.MastermindId, recruit(3, MastermindSession.NextCommanderId));
                before = Chest;
                Net.Router.Dispatch(Net.Roles.MastermindId, recruit(3, MastermindSession.NextCommanderId));
                Assert(MastermindSession.Commanders.Count == EconomyConfig.MaxCommanders, "no more than the maximum commanders");
                Equal(Chest, before, "refused recruit costs nothing");
                MastermindSession.SetWarChest(1f); MastermindSession.Commanders.RemoveAt(3); MastermindUpgradeSave.Save();
                Net.Router.Dispatch(Net.Roles.MastermindId, recruit(3, MastermindSession.NextCommanderId));
                Assert(MastermindSession.Commanders.Count == 3, "can't recruit without the war chest gold");
                MastermindSession.Reset(); MastermindUpgradeSave.EnsureCampaign();
                Assert(MastermindSession.Commanders.Count == 3 && MastermindSession.NextCommanderId == 5, "roster reloaded from the save");
                MastermindSession.Commanders[0].rest = 2; MastermindUpgradeSave.Save();
                GameAccess.CurrentIsland = island; Economy.Begin(island);
                Assert(MastermindSession.Commanders[0].rest == 1, "a new island counts rest down");
                Economy.Restart();
                Assert(MastermindSession.Commanders[0].rest == 2, "retry restores the roster from before the countdown");
                Economy.Begin(island);
                Assert(MastermindSession.Commanders[0].rest == 1, "a retry counts down once, not twice");
                // Older saves (no roster) still load, keeping their gold.
                string v1 = Path.Combine(Path.Combine(directory, "BadNorthMP-Upgrades"),
                    "2-100-42-" + Voxels.TowerDefense.Profile.activeCampaignMeta.campaignNumber + ".bin");
                using (var writer = new BinaryWriter(File.Create(v1))) { writer.Write(0x3155504D); writer.Write(500f); writer.Write(new byte[24]); }
                Economy.End(); MastermindSession.Reset(); MastermindUpgradeSave.EnsureCampaign();
                Assert(MastermindSession.Commanders.Count == 0 && MastermindSession.NextCommanderId == 1, "version 1 save loads without commanders");
                Equal(Chest, 500f, "version 1 save keeps its war chest");

                // Each commander owned makes the next dearer; selling gives back a quarter of what it cost.
                Fixture(false);
                Net.Router.Dispatch(Net.Roles.MastermindId, recruit(0, MastermindSession.NextCommanderId));
                before = Chest;
                Net.Router.Dispatch(Net.Roles.MastermindId, recruit(3, MastermindSession.NextCommanderId));
                Assert(MastermindSession.Commanders.Count == 2 && MastermindSession.Commanders[0].price == CommanderRoster.Price(1, 0) &&
                    MastermindSession.Commanders[1].price == CommanderRoster.Price(1, 3, 1), "the second commander costs more, and both prices are kept");
                Equal(Chest, before - CommanderRoster.Price(1, 3, 1), "the dearer price is what's paid");
                Func<int, byte[]> sellRequest = id => Net.Packet.Build(Net.MsgType.CommanderSell, w => { w.Write(MastermindSession.UpgradeSession); w.Write(id); });
                int soldId = MastermindSession.Commanders[0].id, soldPrice = MastermindSession.Commanders[0].price;
                before = Chest;
                Net.Router.Dispatch(Net.Roles.MastermindId, sellRequest(soldId));
                Assert(MastermindSession.Commander(soldId) == null && MastermindSession.Commanders.Count == 1, "a commander can be sold");
                Equal(Chest, before + soldPrice / 4, "it sells for a quarter of its price");
                before = Chest;
                Net.Router.Dispatch(Net.Roles.MastermindId, sellRequest(soldId));
                Equal(Chest, before, "selling it twice pays once");
                Net.Router.Dispatch(new Steamworks.CSteamID(999), sellRequest(MastermindSession.Commanders[0].id));
                Assert(MastermindSession.Commanders.Count == 1, "only the mastermind can sell");
                MastermindSession.Reset(); MastermindUpgradeSave.EnsureCampaign();
                Assert(MastermindSession.Commanders.Count == 1 && MastermindSession.Commanders[0].price == CommanderRoster.Price(1, 3, 1), "the price is saved with the roster");
                GameAccess.CurrentIsland = island; Economy.Begin(island);
                int onIsland = MastermindSession.Commanders[0].id;
                Economy.DeployedCommanders[onIsland] = null;
                before = Chest;
                Net.Router.Dispatch(Net.Roles.MastermindId, sellRequest(onIsland));
                Assert(MastermindSession.Commander(onIsland) != null, "a commander on the island can't be sold");
                Equal(Chest, before, "a refused sale pays nothing");
                Economy.DeployedCommanders.Clear();
                Net.Router.Dispatch(Net.Roles.MastermindId, sellRequest(onIsland));
                Assert(MastermindSession.Commanders.Count == 0, "a commander off the island sells mid-fight too");
                Economy.End();

                Fixture(false);
                MastermindSession.AddCommander(2);
                Net.MastermindNet.Update();
                byte[] state = Net.SteamSession.Instance.sent.Find(p => p[0] == 42);
                Assert(state != null, "map state sent without battle");
                Net.Roles.IsDefender = false; Net.Roles.IsMastermind = true;
                Net.Router.Dispatch(Net.Roles.DefenderId, state);
                Assert(Net.MastermindNet.HasFreshState && Net.MastermindNet.State.upgrades.canBuy, "actual state serialization roundtrip");
                Assert(Net.MastermindNet.State.upgrades.commanders.Count == 1 && Net.MastermindNet.State.upgrades.commanders[0].type == 2 &&
                    Net.MastermindNet.State.upgrades.commanders[0].status == CommanderStatus.Ready && Net.MastermindNet.State.upgrades.nextCommanderId == 2, "roster reaches the mastermind");
                Assert(Net.MastermindNet.State.upgrades.commanders[0].trait == (int)VikingTrait.Iron, "the commander's trait reaches the mastermind");
                long latestSequence = Net.MastermindNet.State.sequence;
                var reordered = Net.MastermindNet.State; reordered.sequence--;
                Net.Router.Dispatch(Net.Roles.DefenderId, Net.Packet.Build(Net.MsgType.MasterState, reordered.Write));
                Assert(Net.MastermindNet.State.sequence == latestSequence, "out-of-order snapshot rejected");
                byte[] malformedState = (byte[])state.Clone(); Array.Resize(ref malformedState, malformedState.Length - 1);
                Net.Router.Dispatch(Net.Roles.DefenderId, malformedState);
                Assert(Net.MastermindNet.State.sequence == latestSequence, "truncated state ignored without clobbering state");
                Net.MastermindNet.RequestUpgrade(sword, UnitUpgrade.Damage);
                Assert(Net.MastermindNet.UpgradePending, "request pending prevents double-clicks");
                byte[] buyPacket = Net.SteamSession.Instance.sent[Net.SteamSession.Instance.sent.Count - 1];
                Assert(buyPacket[0] == 48 && buyPacket.Length == 19, "BuyUpgrade uses exact approved wire layout");
                int sentCount = Net.SteamSession.Instance.sent.Count;
                Net.MastermindNet.RequestUpgrade(sword, UnitUpgrade.Damage);
                Assert(Net.SteamSession.Instance.sent.Count == sentCount, "pending duplicate not sent");
                UnityEngine.Time.unscaledTime = 3f;
                Assert(!Net.MastermindNet.HasFreshState, "stale state disables purchases");

                Console.WriteLine("Mastermind upgrade actual-source tests passed: " + assertions + " assertions.");
            }
            finally
            {
                Net.Roles.IsDefender = false;
                string resolved = Path.GetFullPath(directory);
                string allowed = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "bnmp-upgrade-tests-");
                if (!resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe test cleanup path");
                Directory.Delete(resolved, true);
            }
        }
    }
}
