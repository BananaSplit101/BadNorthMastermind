using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public bool destroyed;
        public static implicit operator bool(Object o) => o != null && !o.destroyed;
        public static void Destroy(Object o) { if (o != null) o.destroyed = true; }
        public static GameObject Instantiate(GameObject source, Transform parent)
        {
            var copy = new GameObject(source.name); copy.activeSelf = source.activeSelf;
            foreach (Component c in source.components)
            {
                var clone = (Component)Activator.CreateInstance(c.GetType()); copy.Attach(clone);
                if (c is Voxels.TowerDefense.SelfPoolingPrefab pooled)
                    ((Voxels.TowerDefense.SelfPoolingPrefab)clone).owner = pooled.owner;
            }
            copy.transform.SetParent(parent, false); return copy;
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T[] GetComponents<T>() where T : Component => gameObject.GetComponents<T>();
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component => gameObject.GetComponentsInChildren<T>(inactive);
        public T GetComponentInParent<T>() where T : Component
        {
            for (Transform tr = transform; tr != null; tr = tr.parent) { T c = tr.GetComponent<T>(); if (c) return c; }
            return null;
        }
    }
    public class Behaviour : Component { public bool enabled = true; public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy; }
    public class MonoBehaviour : Behaviour { }
    public class GameObject : Object
    {
        public readonly List<Component> components = new List<Component>();
        public Transform transform;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public GameObject(string n) { name = n; transform = new Transform(); Attach(transform); }
        public void Attach(Component c) { c.gameObject = this; c.name = name; components.Add(c); Resources.all.Add(c); }
        public T Add<T>() where T : Component, new() { var c = new T(); Attach(c); return c; }
        public void SetActive(bool active) { activeSelf = active; }
        public T GetComponent<T>() where T : Component { foreach (var c in components) if (c is T match) return match; return null; }
        public T[] GetComponents<T>() where T : Component { var result = new List<T>(); foreach (var c in components) if (c is T match) result.Add(match); return result.ToArray(); }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component
        {
            var result = new List<T>(); if (inactive || activeInHierarchy) result.AddRange(GetComponents<T>());
            foreach (Transform tr in transform.children) result.AddRange(tr.gameObject.GetComponentsInChildren<T>(inactive)); return result.ToArray();
        }
    }
    public class Transform : Component
    {
        public readonly List<Transform> children = new List<Transform>();
        public Transform parent;
        public Vector3 localPosition, localScale = Vector3.one;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 position { get => localPosition; set => localPosition=value; }
        public Quaternion rotation { get => localRotation; set => localRotation=value; }
        public Vector3 lossyScale => localScale;
        public int childCount => children.Count;
        public Transform GetChild(int i) => children[i];
        public void SetParent(Transform tr, bool world = false) { if (parent != null) parent.children.Remove(this); parent = tr; if (tr != null) tr.children.Add(this); }
        public bool IsChildOf(Transform tr) { for (var p = parent; p != null; p = p.parent) if (p == tr) return true; return false; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public static Vector3 zero => new Vector3(); public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => new Vector3(Mathf.Lerp(a.x,b.x,t),Mathf.Lerp(a.y,b.y,t),Mathf.Lerp(a.z,b.z,t));
    }
    public struct Quaternion
    {
        public float x,y,z,w;
        public Quaternion(float a,float b,float c,float d) { x=a;y=b;z=c;w=d; }
        public static Quaternion identity => new Quaternion(0,0,0,1);
        public static Quaternion Slerp(Quaternion a,Quaternion b,float t) => new Quaternion(Mathf.Lerp(a.x,b.x,t),Mathf.Lerp(a.y,b.y,t),Mathf.Lerp(a.z,b.z,t),Mathf.Lerp(a.w,b.w,t));
    }
    public struct Color { public float r,g,b,a; public Color(float x,float y,float z,float alpha) {r=x;g=y;b=z;a=alpha;} }
    public static class Mathf
    {
        public static float Clamp(float f,float a,float b) => Math.Max(a,Math.Min(b,f));
        public static float Clamp01(float f) => Clamp(f,0,1);
        public static float Lerp(float a,float b,float t) => a+(b-a)*Clamp01(t);
        public static float InverseLerp(float a,float b,float t) => a==b?0:Clamp01((t-a)/(b-a));
        public static float Repeat(float t,float length) => t-(float)Math.Floor(t/length)*length;
        public static float Ceil(float v) => (float)Math.Ceiling(v);
    }
    public class Renderer : Behaviour { }
    public class SpriteRenderer : Renderer { public Sprite sprite; public Color color; }
    public class Collider : Behaviour { }
    public class Rigidbody : Component { public bool isKinematic,detectCollisions=true; }
    public class Texture : Object { }
    public class Sprite : Object { public Texture texture; }
    public static class Resources
    {
        public static readonly List<Object> all = new List<Object>();
        public static T[] FindObjectsOfTypeAll<T>() where T : Object { var result=new List<T>(); foreach(var o in all) if(o is T t && t)result.Add(t);return result.ToArray(); }
    }
    public enum AnimatorControllerParameterType { Float=1,Int=3,Bool=4,Trigger=9 }
    public class AnimatorControllerParameter { public int nameHash; public AnimatorControllerParameterType type; }
    public struct AnimatorStateInfo { public int fullPathHash; public float normalizedTime; public bool loop; }
    public struct AnimatorTransitionInfo { public float normalizedTime; }
    public enum AnimatorCullingMode { AlwaysAnimate }
    public class Animator : Behaviour
    {
        public Object runtimeAnimatorController = new Object();
        public AnimatorControllerParameter[] parameters = new AnimatorControllerParameter[0];
        public AnimatorStateInfo[] states = new AnimatorStateInfo[2], next = new AnimatorStateInfo[2];
        public bool[] transitioning = new bool[2]; public float[] blends = new float[2],weights=new float[]{1,1};
        public Dictionary<int,object> values = new Dictionary<int,object>();
        public bool fireEvents=true,applyRootMotion=true; public float speed=1; public AnimatorCullingMode cullingMode;
        public int events, updates;
        public bool isInitialized = true;
        public int layerCount => states.Length;
        public bool GetBool(int id) => values.ContainsKey(id) && (bool)values[id];
        public int GetInteger(int id) => values.ContainsKey(id)?(int)values[id]:0;
        public float GetFloat(int id) => values.ContainsKey(id)?(float)values[id]:0;
        public void SetBool(int id,bool v) { values[id]=v; }
        public void SetInteger(int id,int v) { values[id]=v; }
        public void SetFloat(int id,float v) { values[id]=v; }
        public float GetLayerWeight(int l) => weights[l]; public void SetLayerWeight(int l,float v) { weights[l]=v; }
        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int l) => states[l];
        public AnimatorStateInfo GetNextAnimatorStateInfo(int l) => next[l];
        public bool IsInTransition(int l) => transitioning[l];
        public AnimatorTransitionInfo GetAnimatorTransitionInfo(int l) => new AnimatorTransitionInfo{normalizedTime=blends[l]};
        public bool HasState(int l,int hash) => hash>0;
        public void Play(int hash,int l,float phase) { states[l]=new AnimatorStateInfo{fullPathHash=hash,normalizedTime=phase}; }
        public void CrossFade(int hash,float duration,int l,float phase,float blend) { next[l]=new AnimatorStateInfo{fullPathHash=hash,normalizedTime=phase}; blends[l]=blend; }
        public void Update(float dt) { updates++; if(fireEvents)events++; }
    }
}
namespace Voxels.TowerDefense
{
    public class Agent : UnityEngine.MonoBehaviour { public float health=1,maxHealth=1; }
    public class AgentComponent : UnityEngine.MonoBehaviour { }
    public class Island : UnityEngine.Object { public UnityEngine.Transform runContainer; }
    public class AgentState { public bool active; }
    public class ReplenishComponent : UnityEngine.MonoBehaviour { public AgentState indoors = new AgentState(); }
    public class SelfPoolingPrefab : UnityEngine.MonoBehaviour { public SelfPoolingPrefab owner; }
    public class PoolablePrefab : UnityEngine.MonoBehaviour { }
    public class Arsonist { private void SpawnTorch(){} private void Throw(){} }
    public class ThrowComponent { private void SpawnTorch(){} private void Throw(){} }
    public class AxeThrowing { private void ThrowAxe(){} }
    public class Torch : SelfPoolingPrefab { public void Shoot(){} }
}
namespace Voxels.TowerDefense.SpriteMagic
{
    public class BatchedSprite : UnityEngine.MonoBehaviour
    {
        public UnityEngine.Color color; public UnityEngine.Sprite bSprite; public int scales,colors;
        public void UpdateScale(){scales++;} public void UpdateColor(){colors++;}
    }
    public class SpriteAnimator : BatchedSprite { public UnityEngine.Sprite sprite,sprite2; public void SetSprite2(UnityEngine.Sprite s){sprite2=s;} public void SetSprite(){} }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute { public HarmonyPatch(){} public HarmonyPatch(Type t,string s){} }
    public static class AccessTools
    {
        public static MethodInfo Method(Type t,string s) => t.GetMethod(s,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static);
        public static FieldInfo Field(Type t,string s) => t.GetField(s,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
    }
}
namespace BadNorthMP.Spectator { public static class SpectatorSession {public static bool Active;} }
public static class ExtraRenderers {public static void InitializeAll(UnityEngine.Transform t){} }
namespace BadNorthMP { internal static class GameAccess { public static Voxels.TowerDefense.Island CurrentIsland => null; } }
namespace BadNorthMP.Net
{
    internal enum MsgType : byte { }
    internal static class BattleStream { public static readonly List<byte[]> packets=new List<byte[]>(); public static void Send(byte[] p,bool reliable){packets.Add(p);} }
    internal static class Packet {public static byte[] Build(MsgType t,Action<BinaryWriter> body){return UnitVisualStream.Record(w=>{w.Write((byte)t);body(w);});} }
    internal static class BinaryExtensions
    {
        public static void Write(this BinaryWriter w,UnityEngine.Vector3 v){w.Write(v.x);w.Write(v.y);w.Write(v.z);}
        public static UnityEngine.Vector3 ReadVector3(this BinaryReader r)=>new UnityEngine.Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
        public static void WriteRotation(this BinaryWriter w,UnityEngine.Quaternion q){w.Write((short)(q.x*32767));w.Write((short)(q.y*32767));w.Write((short)(q.z*32767));w.Write((short)(q.w*32767));}
        public static UnityEngine.Quaternion ReadRotation(this BinaryReader r)=>new UnityEngine.Quaternion(r.ReadInt16()/32767f,r.ReadInt16()/32767f,r.ReadInt16()/32767f,r.ReadInt16()/32767f);
    }

    public static class UnitVisualTests
    {
        private static void Check(bool value,string message){if(!value)throw new Exception(message);}
        private static bool Near(float a,float b)=>Math.Abs(a-b)<0.002;
        private static UnityEngine.GameObject Child(UnityEngine.GameObject p,string n){var c=new UnityEngine.GameObject(n);c.transform.SetParent(p.transform);return c;}
        private static void Deliver(BadNorthMP.Spectator.UnitVisualReceiver receiver)
        {
            UnitVisualStream.FlushFrames();
            foreach(byte[] packet in BattleStream.packets)
            {
                Check(packet.Length<=1100,"packet budget exceeded");
                using(var r=new BinaryReader(new MemoryStream(packet)))
                {
                    MsgType type=(MsgType)r.ReadByte();
                    if(type==UnitVisualStream.NodeDefinition){Check(r.ReadInt32()==17,"wrong unit id");receiver.Define(r);}
                    else if(type==UnitVisualStream.NodeGone){r.ReadInt32();receiver.Remove(r.ReadUInt16());}
                    else BadNorthMP.Spectator.UnitVisualReceiver.ReceivePacket(type,r,id=>{Check(id==17,"wrong unit id");return receiver;});
                    Check(r.BaseStream.Position==r.BaseStream.Length,"wire decode did not consume packet");
                }
            }
            BattleStream.packets.Clear();
        }
        public static void Run()
        {
            var source=new UnityEngine.GameObject("Agent");var agent=source.Add<Voxels.TowerDefense.Agent>();
            var animation=source.Add<UnityEngine.Animator>();
            animation.states[0]=new UnityEngine.AnimatorStateInfo{fullPathHash=101,normalizedTime=.9f,loop=true};
            animation.states[1]=new UnityEngine.AnimatorStateInfo{fullPathHash=202,normalizedTime=.8f,loop=false};
            animation.weights[1]=.4f;
            animation.parameters=new[]{new UnityEngine.AnimatorControllerParameter{nameHash=1,type=UnityEngine.AnimatorControllerParameterType.Float},new UnityEngine.AnimatorControllerParameter{nameHash=2,type=UnityEngine.AnimatorControllerParameterType.Bool},new UnityEngine.AnimatorControllerParameter{nameHash=3,type=UnityEngine.AnimatorControllerParameterType.Int},new UnityEngine.AnimatorControllerParameter{nameHash=4,type=UnityEngine.AnimatorControllerParameterType.Trigger}};
            animation.SetFloat(1,2);animation.SetBool(2,true);animation.SetInteger(3,9);
            var weapon=Child(source,"Weapon");weapon.transform.localPosition=new UnityEngine.Vector3(0,2,0);
            var sprite=weapon.Add<Voxels.TowerDefense.SpriteMagic.SpriteAnimator>();sprite.enabled=false;sprite.color=new UnityEngine.Color(.2f,.3f,.4f,1);
            sprite.sprite=new UnityEngine.Sprite{name="Injured",texture=new UnityEngine.Texture{name="BodyAtlas"}};
            sprite.sprite2=new UnityEngine.Sprite{name="HeavyShield",texture=new UnityEngine.Texture{name="EquipmentAtlas"}};
            UnityEngine.Resources.all.Add(sprite.sprite);UnityEngine.Resources.all.Add(sprite.sprite2);
            var childAnim=weapon.Add<UnityEngine.Animator>();childAnim.states[0]=new UnityEngine.AnimatorStateInfo{fullPathHash=303,normalizedTime=.25f};
            var duplicate=Child(source,"Weapon");duplicate.transform.localPosition=new UnityEngine.Vector3(0,8,0);duplicate.SetActive(false);
            var copy=new UnityEngine.GameObject("Agent");var puppet=copy.Add<Voxels.TowerDefense.Agent>();var target=copy.Add<UnityEngine.Animator>();target.parameters=animation.parameters;
            var copyWeapon=Child(copy,"Weapon");var copySprite=copyWeapon.Add<Voxels.TowerDefense.SpriteMagic.SpriteAnimator>();var targetChild=copyWeapon.Add<UnityEngine.Animator>();
            var copyDuplicate=Child(copy,"Weapon");
            var sender=new UnitVisualStream.Sender();var receiver=new BadNorthMP.Spectator.UnitVisualReceiver();
            sender.Capture(17,agent,0);Deliver(receiver);
            animation.states[0]=new UnityEngine.AnimatorStateInfo{fullPathHash=101,normalizedTime=.1f,loop=true};
            animation.states[1]=new UnityEngine.AnimatorStateInfo{fullPathHash=202,normalizedTime=.2f,loop=false};
            animation.SetFloat(1,4);weapon.transform.localPosition=new UnityEngine.Vector3(0,4,0);
            sender.Capture(17,agent,.1f);Deliver(receiver);receiver.Apply(puppet,.05f);
            Check(target.states[0].fullPathHash==101 && (Near(target.states[0].normalizedTime,0)||Near(target.states[0].normalizedTime,1)),"loop wrap interpolation wrong");
            Check(Near(target.states[1].normalizedTime,.5f)&&Near(target.weights[1],.4f),"all layers / backwards one-shot seek wrong");
            Check(Near(target.GetFloat(1),3)&&target.GetBool(2)&&target.GetInteger(3)==9&&!target.values.ContainsKey(4),"typed parameter/trigger decode wrong");
            Check(targetChild.states[0].fullPathHash==303,"child animator was not applied");
            Check(Near(copyWeapon.transform.localPosition.y,3)&&Near(copyDuplicate.transform.localPosition.y,8)&&!copyDuplicate.activeSelf,"duplicate child path binding or local transform interpolation wrong");
            Check(!copySprite.enabled&&copySprite.sprite==sprite.sprite&&copySprite.sprite2==sprite.sprite2&&Near(copySprite.color.r,.2f),"injured sprite/equipment/enabled state lost");
            Check(copySprite.scales>0&&copySprite.colors>0,"batched mesh scale/color not refreshed");
            Check(!target.fireEvents&&!target.applyRootMotion&&target.speed==0&&target.events==0&&target.updates>0&&targetChild.events==0,"Animator.Update fired gameplay events");
            // Hidden units still receive health and pose updates; no local state callbacks needed.
            agent.health=.25f;agent.maxHealth=3;source.Add<Voxels.TowerDefense.ReplenishComponent>().indoors.active=true;source.SetActive(false);
            sender.Capture(17,agent,.2f);Deliver(receiver);receiver.Apply(puppet,.2f);
            Check(!copy.activeSelf&&receiver.HasAgentFrame&&!receiver.Visible&&Near(puppet.health,.25f)&&Near(puppet.maxHealth,3),"indoors visibility/health was lost");
            source.SetActive(true);agent.GetComponent<Voxels.TowerDefense.ReplenishComponent>().indoors.active=false;
            animation.states[0]=new UnityEngine.AnimatorStateInfo{fullPathHash=404,normalizedTime=.99f};
            sender.Capture(17,agent,.3f);Deliver(receiver);receiver.Apply(puppet,.3f);
            Check(copy.activeSelf&&target.states[0].fullPathHash==404,"dying animation / reappearance missing");
            animation.transitioning[1]=true;animation.next[1]=new UnityEngine.AnimatorStateInfo{fullPathHash=505,normalizedTime=.6f};animation.blends[1]=.3f;
            sender.Capture(17,agent,.4f);Deliver(receiver);receiver.Apply(puppet,.4f);
            Check(target.next[1].fullPathHash==505&&Near(target.blends[1],.3f),"transition target/blend not replicated");
            // Force packet splitting with a large real sender controller.
            var many=new List<UnityEngine.AnimatorControllerParameter>();for(int i=0;i<400;i++)many.Add(new UnityEngine.AnimatorControllerParameter{nameHash=1000+i,type=UnityEngine.AnimatorControllerParameterType.Float});
            animation.parameters=many.ToArray();target.parameters=animation.parameters;
            sender.Capture(17,agent,.5f);UnitVisualStream.FlushFrames();int animatorPackets=0;foreach(var p in BattleStream.packets)if(p[0]==26)animatorPackets++;
            Check(animatorPackets>1,"large controllers not split");Deliver(receiver);receiver.Apply(puppet,.5f);
            Check(target.values.ContainsKey(1399),"split controller truncated");
            sender.Capture(17,agent,.51f);UnitVisualStream.FlushFrames();int parameterRecords=0;foreach(var p in BattleStream.packets)if(p[0]==26)using(var r=new BinaryReader(new MemoryStream(p))){r.ReadByte();r.ReadSingle();r.ReadInt32();int count=r.ReadUInt16();for(int i=0;i<count;i++){int len=r.ReadUInt16();var record=r.ReadBytes(len);if(record[3]==0)parameterRecords++;}}
            Check(parameterRecords==0,"unchanged params were resent every tick");Deliver(receiver);
            sender.Capture(17,agent,1.1f);UnitVisualStream.FlushFrames();parameterRecords=0;foreach(var p in BattleStream.packets)if(p[0]==26)using(var r=new BinaryReader(new MemoryStream(p))){r.ReadByte();r.ReadSingle();r.ReadInt32();int count=r.ReadUInt16();for(int i=0;i<count;i++){int len=r.ReadUInt16();var record=r.ReadBytes(len);if(record[3]==0)parameterRecords++;}}
            Check(parameterRecords==400,"parameter refresh does not recover packet loss");Deliver(receiver);
            // Held item is cloned visually, never fetched from a pool; owner is cleared before activation.
            var prefab=new UnityEngine.GameObject("Torch");var pooled=prefab.Add<Voxels.TowerDefense.Torch>();pooled.owner=new Voxels.TowerDefense.SelfPoolingPrefab();prefab.Add<UnityEngine.Animator>();prefab.Add<UnityEngine.Collider>();prefab.Add<UnityEngine.Rigidbody>();
            var held=Child(weapon,"Torch(Clone)");held.Add<Voxels.TowerDefense.Torch>();
            sender.Capture(17,agent,1.2f);Deliver(receiver);receiver.Apply(puppet,1.2f);
            UnityEngine.Transform heldCopy=null;foreach(var tr in copyWeapon.transform.children)if(tr.gameObject.name=="Torch(Clone)")heldCopy=tr;
            Check(heldCopy!=null,"held pooled visual missing");
            Check(heldCopy.GetComponent<Voxels.TowerDefense.Torch>().owner==null&&!heldCopy.GetComponent<Voxels.TowerDefense.Torch>().enabled&&pooled.owner!=null,"clone corrupted pool owner / left native Update enabled");
            Check(!heldCopy.GetComponent<UnityEngine.Animator>().fireEvents&&!heldCopy.GetComponent<UnityEngine.Collider>().enabled&&heldCopy.GetComponent<UnityEngine.Rigidbody>().isKinematic,"projectile clone still has gameplay events/physics");
            held.transform.SetParent(null);sender.Capture(17,agent,1.3f);Deliver(receiver);
            Check(heldCopy.gameObject.destroyed,"released held clone not removed");
            // Native equipment may plant outside the agent, even while the agent is hidden indoors.
            var pole=Child(weapon,"FlagPole");var copyPole=Child(copyWeapon,"FlagPole");
            sender.Capture(17,agent,1.4f);Deliver(receiver);receiver.Apply(puppet,1.4f);
            pole.transform.SetParent(null);pole.transform.position=new UnityEngine.Vector3(12,3,4);
            source.SetActive(false);agent.GetComponent<Voxels.TowerDefense.ReplenishComponent>().indoors.active=true;
            sender.Capture(17,agent,1.5f);Deliver(receiver);receiver.Apply(puppet,1.5f);
            Check(!copy.activeSelf&&copyPole.transform.parent==null&&Near(copyPole.transform.position.x,12)&&copyPole.activeInHierarchy,"planted flag did not retain world pose while hero indoors");
            pole.transform.SetParent(weapon.transform);pole.transform.localPosition=new UnityEngine.Vector3(0,1,0);
            source.SetActive(true);agent.GetComponent<Voxels.TowerDefense.ReplenishComponent>().indoors.active=false;
            sender.Capture(17,agent,1.6f);Deliver(receiver);receiver.Apply(puppet,1.6f);
            Check(copyPole.transform.parent==copyWeapon.transform&&Near(copyPole.transform.localPosition.y,1),"flag collect did not restore child parent / pose");
            // Explicit removal must hide a prefab-owned child rather than leave stale equipment visible.
            var standalone=new BadNorthMP.Spectator.UnitVisualReceiver();
            using(var r=new BinaryReader(new MemoryStream(UnitVisualStream.Record(w=>{w.Write((ushort)0);w.Write(ushort.MaxValue);w.Write("Agent");w.Write("");}))))standalone.Define(r);
            using(var r=new BinaryReader(new MemoryStream(UnitVisualStream.Record(w=>{w.Write((ushort)1);w.Write((ushort)0);w.Write("Weapon");w.Write("");}))))standalone.Define(r);
            var resolve=typeof(BadNorthMP.Spectator.UnitVisualReceiver).GetMethod("Resolve",BindingFlags.Instance|BindingFlags.NonPublic);
            resolve.Invoke(standalone,new object[]{(ushort)1,puppet});standalone.Remove(1);
            Check(!copyWeapon.activeSelf&&!copyWeapon.destroyed,"native-bound removed child was left visible or destroyed");
            copyWeapon.SetActive(true);
            // Out-of-order packets cannot roll the selected visibility/health back.
            source.SetActive(false);agent.health=.7f;sender.Capture(17,agent,1.8f);Deliver(receiver);
            source.SetActive(true);agent.health=.2f;sender.Capture(17,agent,1.7f);Deliver(receiver);receiver.Apply(puppet,1.8f);
            Check(!copy.activeSelf&&Near(puppet.health,.7f),"out-of-order visibility overwrote newest frame");
            BadNorthMP.Spectator.SpectatorSession.Active=true;
            var eventPrefix=typeof(BadNorthMP.Spectator.SpectatorNativeVisualEventPatch).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic);
            Check(!(bool)eventPrefix.Invoke(null,null),"native Arsonist/throw event guard did not block spectator");
            int methods=0;foreach(var method in BadNorthMP.Spectator.SpectatorNativeVisualEventPatch.TargetMethods()){Check(method!=null,"missing native Harmony target");methods++;}Check(methods==5,"native handler guard coverage changed");
            var shootPrefix=typeof(BadNorthMP.Spectator.SpectatorTorchShootGuard).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic);
            Check(!(bool)shootPrefix.Invoke(null,null),"Torch.Shoot guard failed");
            BadNorthMP.Spectator.SpectatorSession.Active=false;Check((bool)eventPrefix.Invoke(null,null)&&(bool)shootPrefix.Invoke(null,null),"guards blocked defender native gameplay");
            bool truncated=false;try{using(var ms=new MemoryStream(new byte[]{1,0,8,0,0}))using(var r=new BinaryReader(ms))receiver.Receive(UnitVisualStream.NodeFrame,2,r);}catch(EndOfStreamException){truncated=true;}Check(truncated,"truncated record silently accepted");
        }
    }
}
