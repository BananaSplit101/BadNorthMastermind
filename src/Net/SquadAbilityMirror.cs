using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.Upgrades;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Teammates' ability buttons follow the leader's abilities. A remote defender's squads are copies whose abilities
    /// never run there (the clicks go to the leader), so their buttons never showed a cooldown, never used up a charge
    /// and never saw the squad busy healing: they always looked ready, and the clicks the leader then refused looked like
    /// abilities breaking at random. The leader sends each ability's state (DefenderTracker.SendSquadAbilities); here
    /// the copy's cooldown and charges are set to match, and whatever else stops it on the leader stops it here too.
    /// </summary>
    internal static class SquadAbilityMirror
    {
        public const byte Cooling = 1, Blocked = 2;
        // How long the leader's "blocked" holds without news (it re-sends everything every 2 s).
        private const float Stale = 5f;

        public struct State
        {
            public int hash;
            public byte flags;
            public float cooldown;
            public short charges;
        }

        private static readonly Dictionary<ActiveAbility, float> blockedUntil = new Dictionary<ActiveAbility, float>();
        private static readonly FieldInfo Charges = AccessTools.Field(typeof(ActiveAbility), "_chargesRemaining");
        private static readonly MethodInfo SetTimeStamp = AccessTools.PropertySetter(typeof(AgentState), "timeStamp");

        public static void Reset() => blockedUntil.Clear();

        /// <summary>Evacuation has its own state (SquadEvac), and joystick movement isn't a button.</summary>
        private static bool Mirrored(ActiveAbility ability) => ability && !(ability is EvacuateAbility) && !(ability is JoystickMoveAbility);

        /// <summary>A name for an ability type that both games agree on (FNV-1a of its full name).</summary>
        public static int Hash(Type type)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in type.FullName) { hash ^= c; hash *= 16777619; }
                return (int)hash;
            }
        }

        // ---- The leader -------------------------------------------------------------------------------------------

        public static List<State> Capture(EnglishSquad squad)
        {
            var states = new List<State>();
            foreach (UpgradeComponent upgrade in squad.upgradeManager)
            {
                var ability = upgrade as ActiveAbility;
                if (!Mirrored(ability) || ability.cooldown == null) continue; // not set up for the battle yet
                byte flags = 0;
                if (ability.isCoolingDown) flags |= Cooling;
                if (BlockedOnLeader(ability, squad)) flags |= Blocked;
                states.Add(new State
                {
                    hash = Hash(ability.GetType()),
                    flags = flags,
                    cooldown = ability.isCoolingDown ? ability.cooldownRemaining : 0f,
                    charges = (short)(ability.hasLimitedCharges ? Mathf.Clamp(ability.chargesRemaining, 0, short.MaxValue) : -1),
                });
            }
            return states;
        }

        /// <summary>The message (MsgType.SquadAbilities): every squad's abilities, by commander.</summary>
        public static void Write(BinaryWriter w, List<KeyValuePair<int, List<State>>> squads)
        {
            int count = Mathf.Min(squads.Count, 255);
            w.Write((byte)count);
            for (int i = 0; i < count; i++)
            {
                w.Write(squads[i].Key);
                int abilities = Mathf.Min(squads[i].Value.Count, 255);
                w.Write((byte)abilities);
                for (int a = 0; a < abilities; a++)
                {
                    State state = squads[i].Value[a];
                    w.Write(state.hash);
                    w.Write(state.flags);
                    w.Write((ushort)Mathf.Clamp(Mathf.RoundToInt(state.cooldown * 100f), 0, ushort.MaxValue));
                    w.Write(state.charges);
                }
            }
        }

        /// <summary>What stops the ability on the leader, apart from the cooldown and uses left (the copy shows those itself).</summary>
        private static bool BlockedOnLeader(ActiveAbility ability, EnglishSquad squad)
        {
            if (ability.isCoolingDown || ability.isOutOfCharges) return false;
            // The leader never selects a teammate's squad, so its Pike Charge formation is only worked out for an order.
            if (ability is PikeChargeAbility) return ability.isBanned || ability.isActive || !squad.upgradeManager.CanActivateAbility();
            // Healing and calling troops: on the way, inside, at full strength or no house free, as the leader sees it.
            if (ability is HouseTargetableAbility) return !ability.IsAvailable();
            return ability.IsBlocked();
        }

        // ---- Teammates ---------------------------------------------------------------------------------------------

        public static void Apply(BinaryReader r)
        {
            bool copies = Roles.IsRemoteDefender && Spectator.SpectatorSession.Active && Profile.campaign != null;
            int squads = r.ReadByte();
            for (int s = 0; s < squads; s++)
            {
                int hero = r.ReadInt32();
                int count = r.ReadByte();
                EnglishSquad squad = copies ? Find(hero) : null;
                for (int i = 0; i < count; i++)
                {
                    int hash = r.ReadInt32();
                    byte flags = r.ReadByte();
                    float cooldown = r.ReadUInt16() / 100f;
                    short charges = r.ReadInt16();
                    ActiveAbility ability = squad ? Find(squad, hash) : null;
                    if (!ability || ability.cooldown == null) continue;
                    if (charges >= 0 && ability.hasLimitedCharges && Charges != null) Charges.SetValue(ability, (int)charges);
                    SetCooldown(ability, (flags & Cooling) != 0 ? cooldown : 0f);
                    if ((flags & Blocked) != 0) blockedUntil[ability] = Time.unscaledTime + Stale;
                    else blockedUntil.Remove(ability);
                }
            }
            if (blockedUntil.Count > 200) blockedUntil.Clear(); // abilities of islands long gone
        }

        /// <summary>The leader says this copy's ability can't be used now (IsBlocked patch).</summary>
        public static bool BlockedHere(ActiveAbility ability) =>
            blockedUntil.Count > 0 && blockedUntil.TryGetValue(ability, out float until) && Time.unscaledTime < until &&
            Roles.IsRemoteDefender && Spectator.SpectatorSession.Active;

        private static EnglishSquad Find(int hero)
        {
            foreach (Squad candidate in GameAccess.PlayerSquads)
                if (candidate is EnglishSquad squad && squad.hero != null && Profile.campaign.heroes.IndexOf(squad.hero) == hero)
                    return squad;
            return null;
        }

        private static ActiveAbility Find(EnglishSquad squad, int hash)
        {
            if (squad.upgradeManager == null) return null;
            foreach (UpgradeComponent upgrade in squad.upgradeManager)
                if (upgrade is ActiveAbility ability && Hash(ability.GetType()) == hash) return ability;
            return null;
        }

        /// <summary>The copy's cooldown, running down from where the leader's is (its button draws it like any other).</summary>
        private static void SetCooldown(ActiveAbility ability, float remaining)
        {
            AgentState state = ability.cooldown;
            if (remaining <= 0f || !ability.hasCooldown)
            {
                if (state.active) state.SetActive(false);
                return;
            }
            // Already showing about that much: leave it, so it doesn't jitter with each update.
            if (state.active && Mathf.Abs(ability.cooldownRemaining - remaining) < 0.3f) return;
            if (!state.active) state.SetActive(true);
            SetTimeStamp?.Invoke(state, new object[] { Time.time - Mathf.Max(0f, ability.cooldownPeriod - remaining) });
        }
    }

    /// <summary>On a teammate's copy, what blocks an ability on the leader blocks it here (every ability's check goes through this).</summary>
    [HarmonyPatch(typeof(ActiveAbility), nameof(ActiveAbility.IsBlocked))]
    internal static class SquadAbilityMirrorPatch
    {
        private static void Postfix(ActiveAbility __instance, ref bool __result)
        {
            if (!__result && SquadAbilityMirror.BlockedHere(__instance)) __result = true;
        }
    }
}
