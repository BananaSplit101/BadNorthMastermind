namespace UnityEngine {
 public enum AnimatorControllerParameterType { Float=1, Int=3, Bool=4, Trigger=9 }
 public enum AnimatorCullingMode { AlwaysAnimate }
 public class AnimatorControllerParameter { public int nameHash; public AnimatorControllerParameterType type; }
 public struct AnimatorStateInfo { public int fullPathHash; public float normalizedTime; }
 public static class Mathf { public static int Min(int a,int b) => System.Math.Min(a,b); }
 public class Animator {
  public static implicit operator bool(Animator a) => a != null;
  public int layerCount=2, updateCalls;
  public bool fireEvents=true, applyRootMotion=true;
  public float speed=1f;
  public AnimatorCullingMode cullingMode;
  public int[] hashes={101,102}; public float[] times={1.2f,.3f}, weights={1f,.8f};
  private bool flag; private int count; private float progress;
  public AnimatorControllerParameter[] parameters = {
   new AnimatorControllerParameter { nameHash=11,type=AnimatorControllerParameterType.Bool },
   new AnimatorControllerParameter { nameHash=12,type=AnimatorControllerParameterType.Int },
   new AnimatorControllerParameter { nameHash=13,type=AnimatorControllerParameterType.Float },
   new AnimatorControllerParameter { nameHash=14,type=AnimatorControllerParameterType.Trigger }
  };
  public AnimatorStateInfo GetCurrentAnimatorStateInfo(int i) => new AnimatorStateInfo { fullPathHash=hashes[i], normalizedTime=times[i] };
  public float GetLayerWeight(int i) => weights[i];
  public void SetLayerWeight(int i,float v) { weights[i]=v; }
  public void Play(int hash,int layer,float time) { hashes[layer]=hash; times[layer]=time; }
  public bool GetBool(int hash) { if(hash!=11) throw new System.Exception("Trigger was read as bool"); return flag; }
  public void SetBool(int hash,bool v) { flag=v; }
  public int GetInteger(int hash) => count;
  public void SetInteger(int hash,int v) { count=v; }
  public float GetFloat(int hash) => progress;
  public void SetFloat(int hash,float v) { progress=v; }
  public void Update(float dt) { updateCalls++; }
 }
}
