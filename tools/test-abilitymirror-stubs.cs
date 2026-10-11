using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace HarmonyLib
{
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type t, string method) { } }
    public static class AccessTools
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        public static FieldInfo Field(Type t, string name) => t.GetField(name, All);
        public static MethodInfo PropertySetter(Type t, string name) => t.GetProperty(name, All).GetSetMethod(true);
    }
}
namespace UnityEngine
{
    public class Object
    {
        public bool destroyed;
        public static implicit operator bool(Object o) => o != null && !o.destroyed;
    }
    public static class Time { public static float time, unscaledTime; }
    public static class Mathf
    {
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Abs(float a) => Math.Abs(a);
        public static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));
        public static float Clamp(float v, float lo, float hi) => Math.Max(lo, Math.Min(hi, v));
        public static int RoundToInt(float v) => (int)Math.Round(v);
    }
}
namespace Voxels.TowerDefense
{
    public class AgentState
    {
        bool on;
        public bool active => on;
        public float timeStamp { get; private set; }
        public float timeSinceActivation => on ? UnityEngine.Time.time - timeStamp : 0f;
        public void SetActive(bool value) { if (on != value) { on = value; timeStamp = UnityEngine.Time.time; } }
    }
    public class HeroDefinition { }
    public class UpgradeComponent : UnityEngine.Object { }
    public class SquadUpgradeManager : IEnumerable<UpgradeComponent>
    {
        public List<UpgradeComponent> upgrades = new List<UpgradeComponent>();
        public bool canActivate = true;
        public bool CanActivateAbility() => canActivate;
        public IEnumerator<UpgradeComponent> GetEnumerator() => upgrades.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => upgrades.GetEnumerator();
    }
    public class Squad : UnityEngine.Object { }
    public class EnglishSquad : Squad
    {
        public HeroDefinition hero;
        public SquadUpgradeManager upgradeManager = new SquadUpgradeManager();
        public T Add<T>(T ability) where T : Upgrades.ActiveAbility { ability.manager = upgradeManager; upgradeManager.upgrades.Add(ability); return ability; }
    }
    public class Campaign { public List<HeroDefinition> heroes = new List<HeroDefinition>(); }
    public static class Profile { public static Campaign campaign; }
}
namespace Voxels.TowerDefense.Upgrades
{
    public class ActiveAbility : UpgradeComponent
    {
        public AgentState cooldown = new AgentState();
        public SquadUpgradeManager manager;
        public float period = 30f;
        public int perIsland;
        protected int _chargesRemaining;
        public bool activeNow, banned;
        public int charges { set { perIsland = value; _chargesRemaining = value; } }
        public bool hasCooldown => period > 0f;
        public float cooldownPeriod => period;
        public float cooldownRemaining => UnityEngine.Mathf.Clamp(period - cooldown.timeSinceActivation, 0f, period);
        public bool isCoolingDown => cooldown.active;
        public bool hasLimitedCharges => perIsland > 0;
        public int chargesRemaining => _chargesRemaining;
        public bool isOutOfCharges => hasLimitedCharges && chargesRemaining <= 0;
        public bool isActive => activeNow;
        public bool isBanned => banned;
        // The game's method with the mod's Harmony postfix (SquadAbilityMirrorPatch) applied, as in the game.
        public virtual bool IsBlocked()
        {
            var args = new object[] { this, isBanned || isCoolingDown || isActive || isOutOfCharges || !manager.CanActivateAbility() };
            PatchedPostfix.Invoke(null, args);
            return (bool)args[1];
        }
        public virtual bool IsAvailable() => !IsBlocked();
        static readonly MethodInfo PatchedPostfix = typeof(BadNorthMP.Net.SquadAbilityMirror).Assembly
            .GetType("BadNorthMP.Net.SquadAbilityMirrorPatch").GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static);
    }
    public abstract class TargetableAbility : ActiveAbility { }
    public abstract class NavSpotTargetableAbility : TargetableAbility { }
    public class BombAbility : NavSpotTargetableAbility { }
    public class PikeChargeAbility : NavSpotTargetableAbility
    {
        public bool inFormation;
        public override bool IsBlocked() => base.IsBlocked() || !inFormation;
    }
    public abstract class HouseTargetableAbility : TargetableAbility
    {
        public object location;
        public bool full, noHouse;
        public override bool IsAvailable() => base.IsAvailable() && location == null && !full && !noHouse;
    }
    public class ReplenishAbility : HouseTargetableAbility { }
    public class InvulnerableAbility : ActiveAbility { }
    public class EvacuateAbility : TargetableAbility { }
    public class JoystickMoveAbility : ActiveAbility { }
}
namespace BadNorthMP
{
    internal static class GameAccess { public static List<Voxels.TowerDefense.Squad> PlayerSquads = new List<Voxels.TowerDefense.Squad>(); }
}
namespace BadNorthMP.Net
{
    internal static class Roles { public static bool IsRemoteDefender; }
}
namespace BadNorthMP.Spectator
{
    internal static class SpectatorSession { public static bool Active; }
}
namespace BadNorthMP.Tests
{
    using Voxels.TowerDefense;
    using Voxels.TowerDefense.Upgrades;
    using BadNorthMP.Net;
    using Time = UnityEngine.Time;

    public static class AbilityMirrorTests
    {
        static int checks;
        static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
        static bool Near(float a, float b) => Math.Abs(a - b) < 0.05f;

        /// <summary>The copy's IsBlocked as the game sees it (the stub applies the mod's patch like Harmony does).</summary>
        static bool Blocked(ActiveAbility ability) => ability.IsBlocked();

        static byte[] Message(params EnglishSquad[] squads)
        {
            var list = new List<KeyValuePair<int, List<SquadAbilityMirror.State>>>();
            foreach (var squad in squads) list.Add(new KeyValuePair<int, List<SquadAbilityMirror.State>>(Profile.campaign.heroes.IndexOf(squad.hero), SquadAbilityMirror.Capture(squad)));
            using (var stream = new MemoryStream())
            {
                var w = new BinaryWriter(stream);
                SquadAbilityMirror.Write(w, list);
                w.Flush();
                return stream.ToArray();
            }
        }

        static void Apply(byte[] message)
        {
            using (var stream = new MemoryStream(message))
            {
                SquadAbilityMirror.Apply(new BinaryReader(stream));
                Check(stream.Position == stream.Length, "the whole message is read");
            }
        }

        public static string Run()
        {
            Profile.campaign = new Campaign();
            var hero = new HeroDefinition(); Profile.campaign.heroes.Add(new HeroDefinition()); Profile.campaign.heroes.Add(hero);
            Time.time = Time.unscaledTime = 100f;

            // The leader's real squad: a bomb on cooldown, an item with one use left, healing on its way to a house,
            // a pike charge not "in formation" (the leader never selects a teammate's squad), and two kept out.
            var leader = new EnglishSquad { hero = hero };
            var bomb = leader.Add(new BombAbility());
            bomb.cooldown.SetActive(true); Time.time += 18f; // 12 s of 30 left
            var item = leader.Add(new InvulnerableAbility { charges = 2 }); item.charges = 2; Set(item, 1);
            var heal = leader.Add(new ReplenishAbility { location = new object() });
            var pike = leader.Add(new PikeChargeAbility { inFormation = false });
            leader.Add(new EvacuateAbility { activeNow = true });
            leader.Add(new JoystickMoveAbility { activeNow = true });

            var states = SquadAbilityMirror.Capture(leader);
            Check(states.Count == 4, "evacuation (own sync) and joystick movement (no button) aren't mirrored");
            Check(states[0].flags == SquadAbilityMirror.Cooling && Near(states[0].cooldown, 12f), "cooling is sent with the time left, not as blocked");
            Check(states[1].charges == 1 && states[1].flags == 0, "uses left are sent");
            Check(states[2].flags == SquadAbilityMirror.Blocked, "healing already on its way blocks it");
            Check(states[3].flags == 0, "pike formation isn't judged on the leader");
            Check(SquadAbilityMirror.Hash(typeof(BombAbility)) == SquadAbilityMirror.Hash(typeof(BombAbility)) &&
                SquadAbilityMirror.Hash(typeof(BombAbility)) != SquadAbilityMirror.Hash(typeof(PikeChargeAbility)), "type names hash the same everywhere, differently per type");
            byte[] message = Message(leader);

            // The mastermind (not a defender) reads the message and changes nothing.
            var copy = new EnglishSquad { hero = hero };
            var copyBomb = copy.Add(new BombAbility());
            var copyItem = copy.Add(new InvulnerableAbility { charges = 2 });
            var copyHeal = copy.Add(new ReplenishAbility());
            var copyPike = copy.Add(new PikeChargeAbility { inFormation = true });
            GameAccess.PlayerSquads.Add(copy);
            Apply(message);
            Check(!copyBomb.isCoolingDown && copyItem.chargesRemaining == 2 && !Blocked(copyHeal), "only a teammate's copies follow");

            // A teammate's copy: the bomb shows the leader's cooldown, the item its uses, healing can't be clicked.
            Roles.IsRemoteDefender = true; Spectator.SpectatorSession.Active = true;
            Apply(message);
            Check(copyBomb.isCoolingDown && Near(copyBomb.cooldownRemaining, 12f), "the copy's cooldown runs from the leader's");
            Check(Blocked(copyBomb), "cooling blocks it the game's own way");
            Check(copyItem.chargesRemaining == 1, "the copy uses up a charge with the leader");
            Check(Blocked(copyHeal) && !copyHeal.IsAvailable(), "healing on its way can't be ordered again");
            Check(!Blocked(copyPike), "pike charge isn't blocked by the leader's formation guess");
            Time.time += 5f; byte[] later = Message(leader); Time.time += 0.2f; Apply(later);
            Check(Near(copyBomb.cooldownRemaining, 6.8f), "a cooldown already shown runs on by itself (no jitter from each update)");
            Apply(message);
            Check(Near(copyBomb.cooldownRemaining, 12f), "a cooldown that's off by more is set again");

            // The leader's states change: the cooldown ends, the squad reaches full strength in the house.
            bomb.cooldown.SetActive(false); heal.location = null;
            Apply(Message(leader));
            Check(!copyBomb.isCoolingDown && !Blocked(copyBomb), "cooldown over on the leader: over on the copy");
            Check(!Blocked(copyHeal), "healing can be ordered again");

            // Busy with another ability on the leader blocks the rest; it lapses if the leader goes quiet.
            leader.upgradeManager.canActivate = false;
            Apply(Message(leader));
            Check(Blocked(copyBomb) && Blocked(copyItem), "another ability in use blocks the others");
            Time.unscaledTime += 6f;
            Check(!Blocked(copyBomb), "a block lapses without news from the leader");

            // Out of uses: shown through the charges (and blocked by the game itself).
            leader.upgradeManager.canActivate = true; Set(item, 0);
            Apply(Message(leader));
            Check(copyItem.chargesRemaining == 0 && copyItem.isOutOfCharges && Blocked(copyItem), "no uses left");

            // A squad the copy doesn't have is skipped cleanly.
            var other = new EnglishSquad { hero = Profile.campaign.heroes[0] }; other.Add(new BombAbility());
            Apply(Message(other, leader));
            return checks + " ability mirror checks passed against the actual SquadAbilityMirror source.";
        }

        static void Set(ActiveAbility ability, int charges) =>
            typeof(ActiveAbility).GetField("_chargesRemaining", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ability, charges);
    }
}
