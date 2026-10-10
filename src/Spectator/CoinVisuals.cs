using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using RTM.Pools;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.CoinDispensing;
using Voxels.TowerDefense.UI;
using BadNorthMP.Net;

namespace BadNorthMP.Spectator
{
    // Use the game's initialized coin pools so BatchedSprite, Animator and pooling callbacks
    // remain intact. This mirrors presentation only; it never dispenses campaign currency.
    internal sealed class CoinVisuals : MonoBehaviour
    {
        private sealed class Tracked { public int id; public bool sent; public DispensedCoin coin; }
        private sealed class Coin { public DispensedCoin instance; public LocalPool<DispensedCoin> pool; }
        private static readonly Dictionary<DispensedCoin, Tracked> tracked = new Dictionary<DispensedCoin, Tracked>();
        private static readonly Dictionary<int, Coin> copies = new Dictionary<int, Coin>();
        private static readonly Dictionary<string, LocalPool<DispensedCoin>> pools = new Dictionary<string, LocalPool<DispensedCoin>>();
        private static int nextId;
        private static bool wasStreaming;
        private float nextFrame;
        // Last house visual sent per house; unchanged houses are only resent to recover lost datagrams.
        private static byte[][] housesSent = new byte[0][];
        private static float[] houseRefresh = new float[0];
        private const float HouseRefresh = 1f;

        public static void Removed(DispensedCoin coin)
        {
            if (!SpectatorSession.Active && BattleStream.Active && !tracked.ContainsKey(coin))
                tracked.Add(coin, new Tracked { id = ++nextId, coin = coin });
        }

        public static void Returned(DispensedCoin coin)
        {
            if (!tracked.TryGetValue(coin, out Tracked item)) return;
            if (item.sent && BattleStream.Active)
                BattleStream.Send(Packet.Build(MsgType.CoinGone, w => w.Write(item.id)), true);
            tracked.Remove(coin);
        }

        public static void Reset()
        {
            foreach (var pool in pools.Values)
            {
                pool.ReturnAll();
                foreach (var item in pool.available)
                    if (item) Object.Destroy(item.gameObject);
            }
            copies.Clear();
            pools.Clear();
        }

        private void LateUpdate()
        {
            if (!BattleStream.Active || SpectatorSession.Active)
            {
                if (wasStreaming) { tracked.Clear(); housesSent = new byte[0][]; houseRefresh = new float[0]; }
                wasStreaming = false;
                return;
            }
            wasStreaming = true;
            if (Time.unscaledTime < nextFrame) return;
            nextFrame = Time.unscaledTime + 0.05f;
            // Pool callbacks also cover teaser coins, checkpoint tokens and philosopher coins.
            foreach (Tracked t in tracked.Values)
            {
                if (!t.coin || !t.coin.gameObject.activeInHierarchy) continue;
                var coin = t.coin;
                BattleStream.Send(Packet.Build(MsgType.CoinFrame, w =>
                {
                    w.Write(t.id); w.Write(coin.name.Replace("(Clone)", "").Trim());
                    w.Write(coin.transform.position); w.WriteRotation(coin.transform.rotation);
                    w.Write(coin.transform.lossyScale);
                    AnimationCodec.Write(w, coin.GetComponent<Animator>());
                }), !t.sent);
                t.sent = true;
            }
            Island island = GameAccess.CurrentIsland;
            if (!island || !island.village) return;
            House[] houses = island.village.houses;
            if (housesSent.Length != houses.Length) { housesSent = new byte[houses.Length][]; houseRefresh = new float[houses.Length]; }
            for (int i = 0; i < houses.Length; i++)
            {
                int index = i;
                House house = houses[i];
                byte[] packet = Packet.Build(MsgType.HouseVisual, w =>
                {
                    w.Write(index);
                    w.Write(Traverse.Create(house).Field("smoothFire").GetValue<float>());
                    AnimationCodec.Write(w, house.animator);
                    AnimationCodec.Write(w, house.uiAnimator);
                    var ui = house.GetComponentInChildren<SquadReplenishUI>(true);
                    Transform visibility = ui ? Traverse.Create(ui).Field("visibility").GetValue<Transform>() : null;
                    w.Write((bool)visibility);
                    if (!visibility) return;
                    Transform scaler = Traverse.Create(ui).Field("scaler").GetValue<Transform>();
                    w.Write(visibility.gameObject.activeSelf); w.Write(scaler.localScale);
                    WriteBar(w, Traverse.Create(ui).Field("startUnitsProgress").GetValue<ProgressBar>());
                    WriteBar(w, Traverse.Create(ui).Field("healedUnitsProgress").GetValue<ProgressBar>());
                });
                // Burning houses animate every frame; otherwise only the idle loop's clock moves.
                bool burning = System.BitConverter.ToSingle(packet, 5) > 0f;
                if (!burning && Time.unscaledTime < houseRefresh[i] && SameIgnoringLayerTimes(packet, housesSent[i])) continue;
                housesSent[i] = packet;
                houseRefresh[i] = Time.unscaledTime + HouseRefresh;
                BattleStream.Send(packet, false);
            }
        }

        /// <summary>
        /// Equal apart from the house animator's layer clocks. Layout: type, int index, float fire, then
        /// AnimationCodec: byte layers, per layer (int hash, float time, float weight).
        /// </summary>
        private static bool SameIgnoringLayerTimes(byte[] a, byte[] b)
        {
            if (b == null || a.Length != b.Length || a.Length < 10) return false;
            int layers = a[9];
            for (int i = 0; i < a.Length; i++)
            {
                int slot = i - 10;
                if (slot >= 0 && slot < layers * 12 && slot % 12 >= 4 && slot % 12 < 8) continue;
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private static void WriteBar(BinaryWriter w, ProgressBar bar)
        {
            w.Write(bar.ratio);
            Color c = bar.image.color;
            w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a);
        }
        private static void ReadBar(BinaryReader r, ProgressBar bar)
        {
            float ratio = r.ReadSingle();
            Color color = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            if (bar) { bar.ratio = ratio; bar.image.color = color; }
        }

        public static void Receive(MsgType type, BinaryReader r)
        {
            if (type == MsgType.CoinGone)
            {
                int id = r.ReadInt32();
                if (copies.TryGetValue(id, out Coin copy))
                { if (copy.instance) copy.pool.ReturnToPool(copy.instance); copies.Remove(id); }
                return;
            }
            if (type == MsgType.CoinFrame)
            {
                int id = r.ReadInt32(); string name = r.ReadString();
                Vector3 pos = r.ReadVector3(); Quaternion rot = r.ReadRotation(); Vector3 scale = r.ReadVector3();
                if (!copies.TryGetValue(id, out Coin copy))
                {
                    var dispenser = Singleton<CoinDispenser>.instance;
                    if (!dispenser) return;
                    if (!pools.TryGetValue(name, out LocalPool<DispensedCoin> pool))
                    {
                        var token = Traverse.Create(dispenser).Field("checkpointTokenPrefab").GetValue<DispensedCoin>();
                        var gold = Traverse.Create(dispenser).Field("goldCoinPrefab").GetValue<DispensedCoin>();
                        var prefab = token && token.name == name ? token : gold;
                        if (!prefab) return;
                        pools[name] = pool = new LocalPool<DispensedCoin>(prefab, dispenser.transform);
                    }
                    copies[id] = copy = new Coin { instance = pool.GetInstance(), pool = pool };
                }
                copy.instance.transform.SetPositionAndRotation(pos, rot);
                copy.instance.transform.localScale = scale;
                AnimationCodec.Read(r, copy.instance.GetComponent<Animator>());
                return;
            }
            int houseIndex = r.ReadInt32();
            var island = GameAccess.CurrentIsland;
            if (!island || houseIndex < 0 || houseIndex >= island.village.houses.Length) return;
            House house = island.village.houses[houseIndex];
            float smoothFire = r.ReadSingle();
            var smoke = Traverse.Create(house).Field("windFire").GetValue<WindSmoke>();
            if (smoke)
            {
                smoke.gameObject.SetActive(smoothFire > 0f);
                smoke.SetMaterialProperty(Shader.PropertyToID("_Fire"), smoothFire);
            }
            AnimationCodec.Read(r, house.animator);
            AnimationCodec.Read(r, house.uiAnimator);
            if (!r.ReadBoolean()) return;
            bool visible = r.ReadBoolean(); Vector3 size = r.ReadVector3();
            var uiCopy = house.GetComponentInChildren<SquadReplenishUI>(true);
            if (!uiCopy) return;
            var view = Traverse.Create(uiCopy);
            var visibilityCopy = view.Field("visibility").GetValue<Transform>();
            visibilityCopy.gameObject.SetActive(visible);
            view.Field("scaler").GetValue<Transform>().localScale = size;
            ReadBar(r, view.Field("startUnitsProgress").GetValue<ProgressBar>());
            ReadBar(r, view.Field("healedUnitsProgress").GetValue<ProgressBar>());
        }

        // All layers and persistent parameters are needed for coin Red overlays and house UI.
        private static class AnimationCodec
        {
            public static void Write(BinaryWriter w, Animator a)
            {
                int layers = a ? Mathf.Min(a.layerCount, 32) : 0;
                w.Write((byte)layers);
                for (int l = 0; l < layers; l++)
                {
                    var state = a.GetCurrentAnimatorStateInfo(l);
                    w.Write(state.fullPathHash); w.Write(state.normalizedTime); w.Write(a.GetLayerWeight(l));
                }
                var parameters = a ? a.parameters : new AnimatorControllerParameter[0];
                int count = Mathf.Min(parameters.Length, 64); w.Write((byte)count);
                for (int i = 0; i < count; i++)
                {
                    var p = parameters[i]; w.Write(p.nameHash); w.Write((byte)p.type);
                    if (p.type == AnimatorControllerParameterType.Float) w.Write(a.GetFloat(p.nameHash));
                    else if (p.type == AnimatorControllerParameterType.Int) w.Write(a.GetInteger(p.nameHash));
                    else w.Write(p.type == AnimatorControllerParameterType.Bool && a.GetBool(p.nameHash));
                }
            }
            public static void Read(BinaryReader r, Animator a)
            {
                // House animation events invoke Done(), which changes village bookkeeping.
                // Remote clips must remain presentation-only, like the agent puppets.
                if (a) { a.fireEvents = false; a.applyRootMotion = false; a.speed = 0f; }
                int layers = r.ReadByte();
                for (int l = 0; l < layers; l++)
                {
                    int hash = r.ReadInt32(); float time = r.ReadSingle(); float weight = r.ReadSingle();
                    if (a && l < a.layerCount)
                    { a.SetLayerWeight(l, weight); if (hash != 0) a.Play(hash, l, time); }
                }
                int count = r.ReadByte();
                for (int i = 0; i < count; i++)
                {
                    int hash = r.ReadInt32(); var type = (AnimatorControllerParameterType)r.ReadByte();
                    if (type == AnimatorControllerParameterType.Float) { float v = r.ReadSingle(); if (a) a.SetFloat(hash, v); }
                    else if (type == AnimatorControllerParameterType.Int) { int v = r.ReadInt32(); if (a) a.SetInteger(hash, v); }
                    else { bool v = r.ReadBoolean(); if (a && type == AnimatorControllerParameterType.Bool) a.SetBool(hash, v); }
                }
                if (a) { a.cullingMode = AnimatorCullingMode.AlwaysAnimate; a.Update(0f); }
            }
        }
    }

    [HarmonyPatch(typeof(DispensedCoin), "RTM.Pools.IPoolable.OnRemoved")]
    internal static class CoinRemovedPatch
    { private static void Postfix(DispensedCoin __instance) => CoinVisuals.Removed(__instance); }
    [HarmonyPatch(typeof(DispensedCoin), "RTM.Pools.IPoolable.OnReturned")]
    internal static class CoinReturnedPatch
    { private static void Prefix(DispensedCoin __instance) => CoinVisuals.Returned(__instance); }
}
