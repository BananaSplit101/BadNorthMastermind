using System;
using System.Collections.Generic;
using System.IO;
using BadNorthMP.Net;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.SpriteMagic;

namespace BadNorthMP.Spectator
{
    internal sealed class UnitVisualReceiver
    {
        private sealed class Definition
        {
            public ushort parent;
            public string name, prefab;
            public Transform transform;
            public bool owned;
            public Animator[] animators;
            public Renderer[] renderers;
            public SpriteAnimator sprite;
            public SpriteRenderer spriteRenderer;
            public BatchedSprite batch;
            public bool meshApplied;
            public Vector3 meshScale;
            public Color meshColor;
        }
        private sealed class Frame
        {
            public float time;
            public byte[] data;
            private BinaryReader primary, secondary;
            public BinaryReader Read(bool next = false)
            {
                BinaryReader reader;
                if (next) reader = secondary ?? (secondary = new BinaryReader(new MemoryStream(data)));
                else reader = primary ?? (primary = new BinaryReader(new MemoryStream(data)));
                reader.BaseStream.Position = 0;
                return reader;
            }
            public void Dispose()
            {
                if (primary != null) primary.Close();
                if (secondary != null) secondary.Close();
            }
        }
        private readonly Dictionary<ushort, Definition> definitions = new Dictionary<ushort, Definition>();
        // Sample histories, keyed by RootKey, NodeKey(index) or AnimatorKey(node, component, kind, hash or layer).
        private readonly Dictionary<ulong, List<Frame>> frames = new Dictionary<ulong, List<Frame>>();
        private static readonly Dictionary<int, Sprite> sprites = new Dictionary<int, Sprite>();
        private static bool spritesIndexed;
        private static Island spriteIsland;
        private static long nextSpriteRefresh;
        private readonly HashSet<Transform> bound = new HashSet<Transform>();
        private readonly HashSet<Animator> evaluated = new HashSet<Animator>();
        private readonly Dictionary<Animator, Dictionary<int, AnimatorControllerParameterType>> parameterTypes = new Dictionary<Animator, Dictionary<int, AnimatorControllerParameterType>>();
        private readonly Dictionary<ulong, Frame> applied = new Dictionary<ulong, Frame>();
        private readonly List<ulong> staleKeys = new List<ulong>();

        private const ulong RootKey = 0;
        private static ulong NodeKey(ushort index) => (1UL << 60) | index;
        private static ulong AnimatorKey(ushort node, byte component, byte kind, uint value) =>
            (2UL << 60) | ((ulong)node << 42) | ((ulong)component << 34) | ((ulong)kind << 32) | value;
        private static bool IsNodeKey(ulong key) => key >> 60 == 1;
        private static ushort NodeOf(ulong key) => (ushort)(IsNodeKey(key) ? key : key >> 42);
        private readonly HashSet<ushort> removed = new HashSet<ushort>();
        private float lastPoseTime = float.NegativeInfinity;
        private bool urgentPose;
        public bool HasAgentFrame { get; private set; }
        public bool Visible { get; private set; } = true;

        public void Define(BinaryReader r)
        {
            ushort index = r.ReadUInt16();
            var definition = new Definition { parent = r.ReadUInt16(), name = r.ReadString(), prefab = r.ReadString() };
            if (!definitions.ContainsKey(index)) definitions.Add(index, definition);
        }

        public void Remove(ushort index)
        {
            removed.Add(index);
            if (definitions.TryGetValue(index, out Definition d))
            {
                if (d.transform) bound.Remove(d.transform);
                if (d.owned && d.transform) UnityEngine.Object.Destroy(d.transform.gameObject);
                else if (d.transform) d.transform.gameObject.SetActive(false);
                if (d.animators != null)
                    foreach (Animator animator in d.animators) { parameterTypes.Remove(animator); evaluated.Remove(animator); }
                definitions.Remove(index);
            }
            // Remove descendant definitions too, never bind them onto a future pooled object.
            var children = new List<ushort>();
            foreach (var entry in definitions) if (entry.Value.parent == index) children.Add(entry.Key);
            foreach (ushort child in children) Remove(child);
            staleKeys.Clear();
            foreach (ulong key in frames.Keys)
                if (key != RootKey && NodeOf(key) == index) staleKeys.Add(key);
            foreach (ulong key in staleKeys)
            {
                foreach (Frame frame in frames[key]) frame.Dispose();
                frames.Remove(key); applied.Remove(key);
            }
        }

        public void Release(Agent agent)
        {
            foreach (Definition d in definitions.Values)
                if (d.transform && d.transform != agent.transform && !d.transform.IsChildOf(agent.transform))
                    UnityEngine.Object.Destroy(d.transform.gameObject);
            foreach (var history in frames.Values)
                foreach (Frame frame in history) frame.Dispose();
            frames.Clear(); applied.Clear(); parameterTypes.Clear(); evaluated.Clear();
        }

        public void Receive(MsgType type, float time, BinaryReader r)
        {
            int count = r.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                int length = r.ReadUInt16();
                byte[] data = r.ReadBytes(length);
                if (data.Length != length) throw new EndOfStreamException();
                // Keys come straight from the record bytes: no reader or string per record.
                ulong key;
                if (type == UnitVisualStream.AgentFrame) key = RootKey;
                else if (type == UnitVisualStream.NodeFrame)
                {
                    if (length < 2) throw new EndOfStreamException();
                    ushort index = BitConverter.ToUInt16(data, 0);
                    if (removed.Contains(index)) continue;
                    key = NodeKey(index);
                }
                else
                {
                    if (length < 6 || (data[3] == 0 && length < 8)) throw new EndOfStreamException();
                    ushort node = BitConverter.ToUInt16(data, 0); byte component = data[2], kind = data[3];
                    if (removed.Contains(node)) continue;
                    key = AnimatorKey(node, component, kind, kind == 0 ? unchecked((uint)BitConverter.ToInt32(data, 4)) : BitConverter.ToUInt16(data, 4));
                }
                if (!frames.TryGetValue(key, out List<Frame> history)) frames.Add(key, history = new List<Frame>());
                if (history.Count == 0) urgentPose = true;
                else if (type == UnitVisualStream.AnimatorFrame && data[3] == 1)
                {
                    byte[] previous = history[history.Count - 1].data;
                    // Layer records: node/component/kind, layer, enabled, weight, state, phase.
                    if (BitConverter.ToInt32(previous, 11) != BitConverter.ToInt32(data, 11) ||
                        BitConverter.ToSingle(data, 15) < BitConverter.ToSingle(previous, 15) ||
                        previous[6] != data[6] || previous[20] != data[20] ||
                        BitConverter.ToSingle(previous, 7) != BitConverter.ToSingle(data, 7) ||
                        (data[20] != 0 && BitConverter.ToInt32(previous, 21) != BitConverter.ToInt32(data, 21))) urgentPose = true;
                }
                else if (type == UnitVisualStream.AgentFrame && history[history.Count - 1].data[0] != data[0]) urgentPose = true;
                int at = history.Count;
                while (at > 0 && history[at - 1].time > time) at--;
                if ((at > 0 && history[at - 1].time == time) || (at < history.Count && history[at].time == time)) continue;
                history.Insert(at, new Frame { time = time, data = data });
                if (history.Count > 8) { history[0].Dispose(); history.RemoveAt(0); }
            }
        }

        private static bool Select(List<Frame> history, float time, out Frame a, out Frame b, out float t)
        {
            a = b = null; t = 0;
            for (int i = history.Count - 1; i >= 0; i--)
                if (history[i].time <= time)
                {
                    a = history[i]; b = i + 1 < history.Count ? history[i + 1] : a;
                    t = a == b ? 0 : Mathf.InverseLerp(a.time, b.time, time); return true;
                }
            return false;
        }

        private Transform Resolve(ushort index, Agent agent)
        {
            if (!definitions.TryGetValue(index, out Definition d)) return null;
            if (d.transform) return d.transform;
            if (d.parent == ushort.MaxValue) { d.transform = agent.transform; Cache(d); return d.transform; }
            Transform parent = Resolve(d.parent, agent);
            if (!parent) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == d.name && !bound.Contains(child)) { d.transform = child; break; }
            }
            if (!d.transform && d.prefab.Length > 0)
            {
                GameObject prefab = VisualOnlyPrefab.Find(d.prefab);
                if (prefab)
                {
                    GameObject go = VisualOnlyPrefab.Clone(prefab, parent);
                    go.name = d.name; d.transform = go.transform; d.owned = true;
                }
            }
            if (d.transform) { bound.Add(d.transform); Cache(d); }
            return d.transform;
        }

        private static void Cache(Definition d)
        {
            d.animators = d.transform.GetComponents<Animator>();
            d.renderers = d.transform.GetComponents<Renderer>();
            d.sprite = d.transform.GetComponent<SpriteAnimator>();
            d.spriteRenderer = d.transform.GetComponent<SpriteRenderer>();
            d.batch = d.transform.GetComponent<BatchedSprite>();
        }

        public void Apply(Agent agent, float time)
        {
            if (frames.TryGetValue(RootKey, out List<Frame> root) && Select(root, time, out Frame rf, out _, out _))
            {
                var r = rf.Read();
                bool active = r.ReadBoolean(); float health = r.ReadSingle(), max = r.ReadSingle(); bool indoors = r.ReadBoolean();
                HasAgentFrame = true; Visible = active && !indoors;
                agent.health = health; agent.maxHealth = max;
                if (agent.gameObject.activeSelf != Visible)
                {
                    agent.gameObject.SetActive(Visible);
                    // OnEnable can reset animation/equipment: reapply everything when a healed unit returns, even if
                    // its samples did not change.
                    applied.Clear();
                }
            }
            // Apply parameters first; states second; evaluate; restore scripted child poses last.
            // Root movement is interpolated by PuppetWorld every frame. Expensive controller
            // seeks and equipment poses need only 30 Hz; new attacks and visibility bypass it.
            if (!urgentPose && time >= lastPoseTime && time - lastPoseTime < 1f / 30f - .0001f) return;
            lastPoseTime = time; urgentPose = false;
            bool evaluatedThisFrame = false;
            for (int pass = 0; pass < 3; pass++)
            {
                evaluated.Clear();
                foreach (var entry in frames)
                {
                    if (entry.Key == RootKey || !Select(entry.Value, time, out Frame a, out Frame b, out float t)) continue;
                    bool node = IsNodeKey(entry.Key);
                    if (node != (pass == 2)) continue;
                    if (!node && a.data[3] != pass) continue;
                    // Frozen animators and unchanged parameters/poses need no repeat evaluation
                    // after their final sample; interpolation still runs between distinct samples.
                    if ((!node || !evaluatedThisFrame) && a == b && applied.TryGetValue(entry.Key, out Frame previous) && previous == a) continue;
                    var r = a.Read(); var next = b.Read(true);
                    ushort index = r.ReadUInt16(); next.ReadUInt16();
                    Transform tr = Resolve(index, agent);
                    if (!tr) continue;
                    if (node) { ApplyNode(r, next, t, tr, tr == agent.transform, agent, definitions[index]); applied[entry.Key] = a == b ? a : null; continue; }
                    byte component = r.ReadByte(), kind = r.ReadByte(); next.ReadByte(); next.ReadByte();
                    if (kind != pass) continue;
                    Animator[] animators = definitions[index].animators;
                    if (component >= animators.Length) continue;
                    Animator animator = animators[component];
                    animator.fireEvents = false; animator.speed = 0; animator.applyRootMotion = false;
                    if (kind == 0) ApplyParameter(r, next, t, animator);
                    else { ApplyLayer(r, next, t, animator); evaluated.Add(animator); }
                    if (kind == 0 || !animator.enabled || animator.isActiveAndEnabled)
                        applied[entry.Key] = a == b ? a : null;
                }
                foreach (Animator animator in evaluated)
                    if (animator.isActiveAndEnabled) { animator.Update(0); evaluatedThisFrame = true; }
            }
        }

        private void ApplyParameter(BinaryReader r, BinaryReader b, float t, Animator animator)
        {
            int hash = r.ReadInt32(); var type = (AnimatorControllerParameterType)r.ReadByte(); b.ReadInt32(); b.ReadByte();
            if (!parameterTypes.TryGetValue(animator, out Dictionary<int, AnimatorControllerParameterType> types))
            {
                types = new Dictionary<int, AnimatorControllerParameterType>();
                foreach (var p in animator.parameters) types[p.nameHash] = p.type;
                parameterTypes.Add(animator, types);
            }
            if (!types.TryGetValue(hash, out AnimatorControllerParameterType known) || known != type)
            {
                // A changed controller can introduce new parameters after the initial binding.
                types.Clear();
                foreach (var p in animator.parameters) types[p.nameHash] = p.type;
                if (!types.TryGetValue(hash, out known) || known != type) return;
            }
            switch (type)
            {
                case AnimatorControllerParameterType.Float: animator.SetFloat(hash, Mathf.Lerp(r.ReadSingle(), b.ReadSingle(), t)); break;
                case AnimatorControllerParameterType.Int: animator.SetInteger(hash, r.ReadInt32()); break;
                case AnimatorControllerParameterType.Bool: animator.SetBool(hash, r.ReadBoolean()); break;
            }
        }

        private static float Phase(float a, float b, bool loop, float t)
        {
            // Preserve complete loop counts. A backwards one-shot seek is never a loop wrap.
            if (loop && b < a && a - b > 0.5f) b += Mathf.Ceil(a - b);
            float value = Mathf.Lerp(a, b, t);
            return loop ? Mathf.Repeat(value, 1) : Mathf.Clamp(value, 0, 0.999f);
        }

        private static void ApplyLayer(BinaryReader r, BinaryReader b, float t, Animator animator)
        {
            int layer = r.ReadUInt16(); b.ReadUInt16(); bool enabled = r.ReadBoolean(); b.ReadBoolean();
            float weight = r.ReadSingle(), endWeight = b.ReadSingle();
            int hash = r.ReadInt32(), endHash = b.ReadInt32(); float phase = r.ReadSingle(), endPhase = b.ReadSingle();
            bool loop = r.ReadBoolean(); b.ReadBoolean();
            bool transition = r.ReadBoolean(), endTransition = b.ReadBoolean();
            if (layer >= animator.layerCount || !animator.runtimeAnimatorController) return;
            animator.enabled = enabled;
            if (!animator.isActiveAndEnabled || hash == 0 || !animator.HasState(layer, hash)) return;
            animator.SetLayerWeight(layer, Mathf.Lerp(weight, endWeight, t));
            animator.Play(hash, layer, Phase(phase, hash == endHash ? endPhase : phase, loop, t));
            if (transition)
            {
                int nextHash = r.ReadInt32(); float nextPhase = r.ReadSingle(); bool nextLoop = r.ReadBoolean(); float blend = r.ReadSingle();
                if (endTransition)
                {
                    int endNext = b.ReadInt32(); float endNextPhase = b.ReadSingle(); b.ReadBoolean(); float endBlend = b.ReadSingle();
                    if (nextHash == endNext && hash == endHash)
                    { nextPhase = Phase(nextPhase, endNextPhase, nextLoop, t); blend = Mathf.Lerp(blend, endBlend, t); }
                }
                if (animator.HasState(layer, nextHash))
                    animator.CrossFade(nextHash, 1f, layer, Phase(nextPhase, nextPhase, nextLoop, 0), Mathf.Clamp01(blend));
            }
        }

        private static Sprite ReadSprite(BinaryReader r, Sprite fallback)
        {
            int key = r.ReadInt32();
            if (key == 0) return null;
            if (fallback && UnitVisualStream.SpriteKey(fallback) == key) return fallback;
            if (spriteIsland != GameAccess.CurrentIsland)
            { spriteIsland = GameAccess.CurrentIsland; sprites.Clear(); spritesIndexed = false; }
            // Runtime-created sprites can appear after the first index: a missing one refreshes it at most every 5 s
            // (shared by every puppet).
            if (!spritesIndexed || (!sprites.ContainsKey(key) && DateTime.UtcNow.Ticks >= nextSpriteRefresh))
            {
                foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                    if (sprite && sprite.texture) sprites[UnitVisualStream.SpriteKey(sprite)] = sprite;
                spritesIndexed = true;
                nextSpriteRefresh = DateTime.UtcNow.Ticks + TimeSpan.TicksPerSecond * 5;
            }
            sprites.TryGetValue(key, out Sprite result);
            return result ? result : fallback;
        }

        private struct NodePose
        {
            public byte flags;
            public ushort parent;
            public Vector3 pos, scale;
            public Quaternion rot;
        }

        /// <summary>The transform part of a node record; left-out fields take their default values.</summary>
        private static NodePose ReadPose(BinaryReader r)
        {
            var pose = new NodePose { flags = r.ReadByte(), parent = r.ReadUInt16(), rot = Quaternion.identity, scale = Vector3.one };
            if ((pose.flags & UnitVisualStream.NodeMoved) != 0) pose.pos = r.ReadVector3();
            if ((pose.flags & UnitVisualStream.NodeRotated) != 0) pose.rot = r.ReadRotation();
            if ((pose.flags & UnitVisualStream.NodeScaled) != 0) pose.scale = r.ReadVector3();
            return pose;
        }

        /// <summary>
        /// A frame datagram: float time, then per unit until the end: int agentId, ushort count, records.
        /// Units without a receiver (not mirrored yet, or already gone) are skipped. Returns the stream time.
        /// </summary>
        public static float ReceivePacket(MsgType type, BinaryReader r, Func<int, UnitVisualReceiver> find)
        {
            float time = r.ReadSingle();
            Stream stream = r.BaseStream;
            while (stream.Position < stream.Length)
            {
                UnitVisualReceiver receiver = find(r.ReadInt32());
                if (receiver != null) { receiver.Receive(type, time, r); continue; }
                int count = r.ReadUInt16();
                for (int i = 0; i < count; i++)
                {
                    int length = r.ReadUInt16();
                    if (stream.Position + length > stream.Length) throw new EndOfStreamException();
                    stream.Position += length;
                }
            }
            return time;
        }

        private void ApplyNode(BinaryReader r, BinaryReader b, float t, Transform tr, bool root, Agent agent, Definition definition)
        {
            NodePose start = ReadPose(r), end = ReadPose(b);
            bool active = (start.flags & UnitVisualStream.NodeActive) != 0;
            bool world = (start.flags & UnitVisualStream.NodeWorld) != 0, endWorld = (end.flags & UnitVisualStream.NodeWorld) != 0;
            if (world != endWorld || start.parent != end.parent) t = 0; // coordinate systems cannot be blended
            if (!root)
            {
                Transform parent = world ? (GameAccess.CurrentIsland ? GameAccess.CurrentIsland.runContainer : null) : Resolve(start.parent, agent);
                if (tr.parent != parent) tr.SetParent(parent, false);
                if (tr.gameObject.activeSelf != active) tr.gameObject.SetActive(active);
                Vector3 pos = Vector3.Lerp(start.pos, end.pos, t); Quaternion rot = Quaternion.Slerp(start.rot, end.rot, t);
                if (world) { tr.position = pos; tr.rotation = rot; }
                else { tr.localPosition = pos; tr.localRotation = rot; }
            }
            Vector3 desiredScale = Vector3.Lerp(start.scale, end.scale, t);
            if (world && tr.parent)
            {
                Vector3 parentScale = tr.parent.lossyScale;
                desiredScale = new Vector3(parentScale.x == 0 ? 0 : desiredScale.x / parentScale.x,
                    parentScale.y == 0 ? 0 : desiredScale.y / parentScale.y, parentScale.z == 0 ? 0 : desiredScale.z / parentScale.z);
            }
            tr.localScale = desiredScale;
            Renderer[] renderers = definition.renderers; int count = r.ReadByte();
            for (int first8 = 0; first8 < count; first8 += 8)
            {
                byte bits = r.ReadByte();
                for (int i = first8; i < count && i < first8 + 8; i++)
                    if (i < renderers.Length) renderers[i].enabled = (bits & (1 << (i - first8))) != 0;
            }
            if ((start.flags & UnitVisualStream.NodeSprite) != 0)
            {
                byte spriteFlags = r.ReadByte(); SpriteAnimator sprite = definition.sprite;
                bool enabled = (spriteFlags & UnitVisualStream.SpriteEnabled) != 0;
                Sprite first = ReadSprite(r, sprite ? sprite.sprite : null), second = ReadSprite(r, sprite ? sprite.sprite2 : null);
                Color color = UnitVisualStream.ReadColor(r, (spriteFlags & UnitVisualStream.SpriteFloatColor) != 0);
                if (sprite)
                {
                    sprite.enabled = enabled; sprite.sprite = first;
                    if (second && sprite.sprite2 != second) sprite.SetSprite2(second);
                    sprite.color = color; sprite.SetSprite();
                }
            }
            if ((start.flags & UnitVisualStream.NodeSpriteRenderer) != 0)
            {
                byte spriteFlags = r.ReadByte();
                SpriteRenderer sr = definition.spriteRenderer; Sprite sprite = ReadSprite(r, sr ? sr.sprite : null);
                Color color = UnitVisualStream.ReadColor(r, (spriteFlags & UnitVisualStream.SpriteFloatColor) != 0);
                if (sr)
                {
                    BatchedSprite batched = definition.batch;
                    if (batched) { if (sprite) batched.bSprite = sprite; batched.color = color; }
                    sr.sprite = sprite; sr.color = color;
                }
            }
            // Giant scaling, spear reach and shadow color also live in generated mesh data.
            BatchedSprite batch = definition.batch;
            if (batch)
            {
                Vector3 actualScale = tr.lossyScale;
                Color actualColor = batch.color;
                if (!definition.meshApplied || actualScale.x != definition.meshScale.x ||
                    actualScale.y != definition.meshScale.y || actualScale.z != definition.meshScale.z)
                    batch.UpdateScale();
                if (!definition.meshApplied || actualColor.r != definition.meshColor.r ||
                    actualColor.g != definition.meshColor.g || actualColor.b != definition.meshColor.b || actualColor.a != definition.meshColor.a)
                    batch.UpdateColor();
                definition.meshApplied = true; definition.meshScale = actualScale; definition.meshColor = actualColor;
            }
        }
    }
}
