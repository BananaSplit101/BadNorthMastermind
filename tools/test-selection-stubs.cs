using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
namespace UnityEngine {
 public class Object {public bool destroyed; public static implicit operator bool(Object value){return value!=null&&!value.destroyed;}}
 public struct Vector2 {}
}
namespace RTM.Utilities { public class WeakReference<T> where T:class { public T Target; } }
namespace HarmonyLib {
 public enum ArgumentType {Normal,Ref,Out,Pointer}
 public class HarmonyPatch:Attribute {public HarmonyPatch(Type t,string method){}public HarmonyPatch(Type t,string method,Type[] args,ArgumentType[] variations){}}
 public class Traverse {
  object target;
  public static Traverse Create(object o){return new Traverse{target=o};}
  public Field<T> Field<T>(string name){return new Field<T>{target=target,name=name};}
 }
 public class Field<T> {internal object target;internal string name;public T Value {get{return (T)target.GetType().GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(target);}}}
}
namespace Voxels.TowerDefense {
 public class Singleton<T> {public static T instance;}
 public class State {public bool active; public Action<bool> onChange;public virtual void SetActive(bool value){active=value;if(onChange!=null)onChange(value);}}
 public class AnimatedState:State {}
 public class Agent:UnityEngine.Object {public int calls;public bool show,godray;public void SetSelection(bool a,bool b){calls++;show=a;godray=b;}}
 public class EnglishSquad:UnityEngine.Object {public AnimatedState selectedState=new AnimatedState();public List<Agent> agents=new List<Agent>();public List<Agent> livingAgents=new List<Agent>();public void ShowSelection(bool show,bool showGodray){throw new Exception("Native unsafe call");}}
 public class CinematicCamera {public static bool isActive;}
 public class SquadSelector:UnityEngine.Object {
  public delegate void OnSquadSelectionChanged(EnglishSquad s);
  public RTM.Utilities.WeakReference<EnglishSquad> _selectedSquad=new RTM.Utilities.WeakReference<EnglishSquad>();
  public OnSquadSelectionChanged onSquadSelectionChanged;
  public State selectionState=new State();
  public EnglishSquad GetSquadFromRaycast(UnityEngine.Vector2 point,out bool wantsHoverEffect){wantsHoverEffect=false;return null;}
 }
 public class Faction:UnityEngine.Object {}
 public class Island:UnityEngine.Object {public Faction vikings=new Faction();}
 public class IslandAmbiance {public Island island;public IEnumerator waterfallUpdate;}
}
namespace Voxels.TowerDefense.UI { public class PauseMenu {public bool AllowSafeExit(){return true;}public bool AllowAbandon(){return true;}} }
namespace Voxels.TowerDefense.Upgrades { public class HouseTargetableAbility:UnityEngine.Object {public Voxels.TowerDefense.EnglishSquad squad;public List<object> locations;public bool IsAvailable(){return true;}} }
namespace BadNorthMP {public static class GameAccess {public static Voxels.TowerDefense.Island CurrentIsland;} }
namespace BadNorthMP.Spectator {public static class SpectatorSession {public static bool Active;} }
namespace BadNorthMP.Net {public static class RemoteDefender {public static Voxels.TowerDefense.EnglishSquad Selected;public static int pending;public static void ForgetSelection(){Selected=null;pending=0;}} }
namespace BadNorthMP.Tests {
 public static class SelectionTests {
  static int checks;
  static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
  static object Call(Type t,params object[] args){return t.GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}
  static IEnumerator Water(){yield return null;}
  public static void Run(){
   var dead=new Voxels.TowerDefense.EnglishSquad{destroyed=true,selectedState=null,agents=null};
   var selector=new Voxels.TowerDefense.SquadSelector();selector._selectedSquad.Target=dead;selector.selectionState.active=true;
   Voxels.TowerDefense.Singleton<Voxels.TowerDefense.SquadSelector>.instance=selector;
   BadNorthMP.Net.RemoteDefender.Selected=dead;BadNorthMP.Net.RemoteDefender.pending=3;
   int notifications=0;
   selector.onSquadSelectionChanged=s=>{Check(s==null&&selector._selectedSquad.Target==null,"Native UI notified before clearing destroyed target");notifications++;};
   selector.selectionState.onChange=active=>{Check(!active&&selector._selectedSquad.Target==null,"State callback still sees destroyed squad");};
   BadNorthMP.Net.SelectionLifecycle.Clear();
   Check(!selector.selectionState.active&&notifications==1,"Selection/UI did not reset");
   Check(BadNorthMP.Net.RemoteDefender.Selected==null&&BadNorthMP.Net.RemoteDefender.pending==0,"Remote pending orders survived wipe");
   BadNorthMP.Net.SelectionLifecycle.Clear();
   Voxels.TowerDefense.Singleton<Voxels.TowerDefense.SquadSelector>.instance=null;
   BadNorthMP.Net.SelectionLifecycle.Clear();
   BadNorthMP.Spectator.SpectatorSession.Active=true;
   var patch=typeof(BadNorthMP.Net.SpectatorSelectionVisualPatch);
   Check(!(bool)Call(patch,dead,false,false),"Destroyed spectator squad was passed to native code");
   var healthy=new Voxels.TowerDefense.EnglishSquad();var agent=new Voxels.TowerDefense.Agent();healthy.agents.Add(agent);healthy.agents.Add(null);healthy.agents.Add(new Voxels.TowerDefense.Agent{destroyed=true});
   Check(!(bool)Call(patch,healthy,true,true)&&healthy.selectedState.active&&agent.calls==1&&agent.godray,"Healthy highlight no longer works");
   Voxels.TowerDefense.CinematicCamera.isActive=true;Call(patch,healthy,false,true);
   Check(!agent.show&&!agent.godray,"Cinematic highlight behavior changed");
   var ability=new Voxels.TowerDefense.Upgrades.HouseTargetableAbility();var result=new object[]{ability,true};
   Check(!(bool)Call(typeof(BadNorthMP.Spectator.SpectatorHealingAvailabilityPatch),result)&&!(bool)result[1],"Stale healing UI not disabled");
   ability.squad=healthy;ability.locations=new List<object>();
   Check((bool)Call(typeof(BadNorthMP.Spectator.SpectatorHealingAvailabilityPatch),ability,false),"Healthy heal availability bypassed");
   var ambiance=new Voxels.TowerDefense.IslandAmbiance();
   Check(!(bool)Call(typeof(BadNorthMP.Spectator.SpectatorAmbianceLifecyclePatch),ambiance),"Wiped ambience still updating");
   ambiance.island=new Voxels.TowerDefense.Island();ambiance.waterfallUpdate=Water();
   Check((bool)Call(typeof(BadNorthMP.Spectator.SpectatorAmbianceLifecyclePatch),ambiance),"Healthy ambience disabled");
   var exit=new object[]{true};Check(!(bool)Call(typeof(BadNorthMP.Spectator.SpectatorPauseExitAvailabilityPatch),exit)&&!(bool)exit[0],"Exit available without island");
   var abandon=new object[]{true};Check(!(bool)Call(typeof(BadNorthMP.Spectator.SpectatorPauseAbandonAvailabilityPatch),abandon)&&!(bool)abandon[0],"Abandon available without island");
   var hover=typeof(BadNorthMP.Spectator.SpectatorSquadHoverPatch).GetMethod("Finalizer",BindingFlags.NonPublic|BindingFlags.Static);
   var hoverArgs=new object[]{new NullReferenceException(),healthy,true};
   Check(hover.Invoke(null,hoverArgs)==null&&hoverArgs[1]==null&&!(bool)hoverArgs[2],"Spectator hover crash not swallowed");
   var other=new InvalidOperationException();Check(hover.Invoke(null,new object[]{other,null,false})==other,"Hover patch hid an unrelated error");
   BadNorthMP.Spectator.SpectatorSession.Active=false;
   Check(hover.Invoke(null,new object[]{new NullReferenceException(),null,false}) is NullReferenceException,"Hover patch hid a defender's error");
   BadNorthMP.Spectator.SpectatorSession.Active=true;
   BadNorthMP.GameAccess.CurrentIsland=new Voxels.TowerDefense.Island();
   Check((bool)Call(typeof(BadNorthMP.Spectator.SpectatorPauseExitAvailabilityPatch),false),"Valid pause menu changed");
   BadNorthMP.Spectator.SpectatorSession.Active=false;
   Check((bool)Call(patch,dead,false,false),"Single-player selection changed");
   Check((bool)Call(typeof(BadNorthMP.Spectator.SpectatorHealingAvailabilityPatch),ability,false),"Single-player healing changed");
   Check((bool)Call(typeof(BadNorthMP.Spectator.SpectatorAmbianceLifecyclePatch),ambiance),"Single-player ambience changed");
   Console.WriteLine(checks+" selection/lifecycle regression checks passed.");
  }
 }
}

