using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
namespace UnityEngine {
 public class Object { public string name; public static implicit operator bool(Object x) {return x!=null;} public static void Destroy(Object x){} }
 public class Component : Object { public Transform transform = new Transform(); public GameObject gameObject = new GameObject(); }
 public class MonoBehaviour : Component { public object StartCoroutine(IEnumerator e){while(e.MoveNext()){} return null;} }
 public class GameObject : Object {public bool activeInHierarchy=true;public T AddComponent<T>() where T:new(){return new T();}}
 public class Transform : Object {public Vector3 position;}
 public struct Vector3 {public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public float sqrMagnitude {get{return x*x+y*y+z*z;}} public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);} }
 public struct Vector2 {public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;} public float sqrMagnitude {get{return x*x+y*y;}}
 public static explicit operator Vector2(Vector3 a){return new Vector2(a.x,a.y);} public static Vector2 operator -(Vector2 a,Vector2 b){return new Vector2(a.x-b.x,a.y-b.y);} }
 public struct Quaternion { public float x,y,z,w;public Quaternion(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}public static Quaternion identity {get{return new Quaternion(0,0,0,1);}} }
 public struct Color {}
 public class Gradient {public Color Evaluate(float value){return new Color();}}
 public static class Mathf {public static float Sqrt(float n){return (float)Math.Sqrt(n);}public static int RoundToInt(float n){return (int)Math.Round(n);}}
 public static class Time {public static float unscaledTime;}
 public static class Resources { public static object[] loaded=new object[0]; public static T[] FindObjectsOfTypeAll<T>(){var l=new List<T>();foreach(var x in loaded) if(x is T)l.Add((T)x);return l.ToArray();} }
 public class Camera {public Vector3 WorldToScreenPoint(Vector3 x){return x;} }
 public enum KeyCode {Alpha1=49,H=104}
 public static class Input {public static Vector3 mousePosition;public static bool GetKeyDown(KeyCode x){return false;}public static bool GetMouseButtonDown(int x){return false;} }
}
namespace SpriteComposing {public class SpriteComposer {public static int created;public SpriteComposer(){created++;}public void MaybeInitalize(){}}}
namespace UnityEngine.EventSystems { public class EventSystem : UnityEngine.Object {public static EventSystem current; public bool IsPointerOverGameObject(){return false;}} }
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class,AllowMultiple=true)]public class HarmonyPatch : Attribute {public HarmonyPatch(Type t,string m,params Type[] a){} }
 public class Traverse {object target;string name; object[] args;public static Traverse Create(object o){return new Traverse{target=o};}
 public FieldValue<T> Field<T>(string n){return new FieldValue<T>{target=target,name=n};}
 public Traverse Method(string n,params object[] a){name=n;args=a;return this;}
 public object GetValue(){return target.GetType().GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Invoke(target,args);}
 public class FieldValue<T>{public object target;public string name;public T Value{get{return (T)target.GetType().GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(target);}}}
 }
}
namespace Steamworks {public struct CSteamID {public ulong m_SteamID; public CSteamID(ulong x){m_SteamID=x;}public static CSteamID Nil{get{return new CSteamID(0);}}
 public static bool operator ==(CSteamID a,CSteamID b){return a.m_SteamID==b.m_SteamID;}public static bool operator !=(CSteamID a,CSteamID b){return !(a==b);}public override bool Equals(object x){return x is CSteamID && this==(CSteamID)x;}public override int GetHashCode(){return m_SteamID.GetHashCode();}}
}
namespace BepInEx {public static class Paths {public static string ConfigPath=Path.Combine(Path.GetTempPath(),"BNMP-party-test-"+Guid.NewGuid());}}
namespace Voxels.TowerDefense {
 public class Singleton<T> where T:new(){public static T instance=new T();}
 public enum Difficulty {None,Easy,Normal,Hard,VeryHard}
 public struct CampaignPrefs{public Difficulty difficulty;public bool skipTutorial,allowReplays;public CampaignPrefs(Difficulty d,bool skip,bool replay){difficulty=d;skipTutorial=skip;allowReplays=replay;}}
 public class State {public bool active;public UnityEngine.GameObject gameObject=new UnityEngine.GameObject();public void SetActive(bool active){gameObject.activeInHierarchy=active;this.active=active;}}
 public class Stack : UnityEngine.Object {public State stateLoading=new State(),stateCampaign=new State(),stateMeta=new State();}
 public class CampaignManager : UnityEngine.Object {public UnityEngine.Object campaign;}
 public class LevelNode {public int index;public HeroDefinition heroDefinition;public void Play(){}}
 public class NavSpotter {public List<NavSpot> navSpots=new List<NavSpot>(); public NavSpot NavSpotCast(UnityEngine.Vector3 p){return navSpots.Count>0?navSpots[0]:null;}}
 public class Island : UnityEngine.Object {public LevelNode levelNode=new LevelNode();public NavSpotter navSpotter=new NavSpotter();}
 public class LevelCamera {public UnityEngine.Camera cameraRef=new UnityEngine.Camera();}
 public class Agent : UnityEngine.Component {public object kind;public T GetComponent<T>() where T:class{return kind as T;}}
 public class Archery : UnityEngine.Object {}public class Spear : UnityEngine.Object {}
 public enum HeroUpgradeTypeEnum {Item,Class,Skill,Consumable,Trait}
 public class HeroUpgradeDefinition : UnityEngine.Object {public UnityEngine.Object upgradeType=new UnityEngine.Object();public HeroUpgradeTypeEnum typeEnum; public string dbgName {get{return name;}}}
 public class HeroClassUpgradeDefinition : HeroUpgradeDefinition {public Agent minionAgentPrefab;}
 public class SerializableHeroUpgrade {public HeroUpgradeDefinition definition; public SerializableHeroUpgrade(){} public SerializableHeroUpgrade(HeroUpgradeDefinition d){definition=d;}}
 public class MonoHero : UnityEngine.Component {public static bool FailSetup;public static int destroyed;public void Setup(HeroDefinition h,List<SerializableHeroUpgrade> u,UnityEngine.GameObject source,int seed,bool crop=true){if(FailSetup)throw new InvalidOperationException("Simulated portrait setup failure");} public void Destroy(){destroyed++;}}
 public class HeroDefinition {public static int setups;public int id;public bool recruited,alive=true,fatigued;public float hue;public UnityEngine.Color color;public MonoHero monoHero;public string dbgName="Test";public SerializableHeroUpgrade classUpgrade,traitUpgrade,itemUpgrade;
 public List<SerializableHeroUpgrade> upgrades=new List<SerializableHeroUpgrade>();public HeroDefinition(int n){id=n;}
 public bool available {get{return recruited&&alive;}}public bool availableThisTurn{get{return available&&!fatigued;}}public bool recruitable{get{return !recruited&&alive;}}
 public void SetupHeroState(bool rec,HeroUpgradeDefinition trait,HeroUpgradeDefinition type,HeroUpgradeDefinition item){setups++;if(item!=null&&item.typeEnum==HeroUpgradeTypeEnum.Consumable)throw new Exception("Consumable purchase side effect during preview");recruited=rec;traitUpgrade=trait==null?null:new SerializableHeroUpgrade{definition=trait};itemUpgrade=item==null?null:new SerializableHeroUpgrade{definition=item};classUpgrade=type!=null?new SerializableHeroUpgrade{definition=type}:null;if(classUpgrade!=null)upgrades.Add(classUpgrade);}
 }
 public class TestInventory {public List<HeroUpgradeDefinition> startingTraits=new List<HeroUpgradeDefinition>(),startingItems=new List<HeroUpgradeDefinition>();}
 public class TestUser {public TestInventory inventory=new TestInventory();}
 public static class Profile {public static TestUser userSave=new TestUser();public static ProfileInternals.CampaignSave campaign;public static ProfileInternals.CampaignSaveMeta activeCampaignMeta;public static int creates,saves;
 public static ProfileInternals.CampaignSave CreateNewCampaign(int seed){creates++;campaign=new ProfileInternals.CampaignSave{seed=seed};activeCampaignMeta=new ProfileInternals.CampaignSaveMeta();return campaign;}public static void SaveCampaign(){saves++;}
 }
 public class NavSpot : UnityEngine.Object {public NavSpotController occupant;}
 public class NavSpotController : UnityEngine.Object {public EnglishSquad enSquad;public bool canMove=true;public NavSpot navSpot;}
 public class Squad : UnityEngine.Component {}
 public class EnglishSquad : Squad {public HeroDefinition hero;public NavSpotController navSpotOccupant=new NavSpotController();public bool selectable=true;public Agent heroAgent=new Agent();public SquadUpgradeManager upgradeManager=new SquadUpgradeManager();public bool selected;public void ShowSelection(bool x){selected=x;}}
 public class SquadSelector : UnityEngine.Object {public EnglishSquad selectedSquad;public void SelectSquad(EnglishSquad s,bool joystick){selectedSquad=s;}}
 public static class SquadMover {public static int moves;public static void MoveTo(EnglishSquad s,NavSpot n){} public static void SwapWith(EnglishSquad s,NavSpotController n){} public static void MoveToDirect(EnglishSquad s,NavSpot n){moves++;s.navSpotOccupant.navSpot=n;}}
 public class SquadUpgradeManager {public List<object> upgrades=new List<object>();public T GetUpgrade<T>() where T:class{foreach(var x in upgrades)if(x is T)return x as T;return null;}}
 public class SquadReplenishLocation : UnityEngine.Component {public bool available=true;}
 public class SquadEvacuationLocation : UnityEngine.Component {public bool available=true;public bool IsAvailable(){return available;}}
 public class SquadSpawner {public static List<HeroDefinition> deployed;public void SpawnEnglishSquads(IEnumerable<HeroDefinition> h){deployed=new List<HeroDefinition>(h);}}
 public class IslandGameplayManager : UnityEngine.Object {public Island island=new Island();public States states=new States();public SquadSpawner squadSpawner=new SquadSpawner();public class States{public State root=new State(),loadout=new State(),Spawning=new State(),Prepare=new State(),running=new State{active=true};}}
}
namespace Voxels.TowerDefense.ProfileInternals {
 public class CampaignSave {public List<Voxels.TowerDefense.HeroUpgradeDefinition> inventory=new List<Voxels.TowerDefense.HeroUpgradeDefinition>();public int seed;public Voxels.TowerDefense.CampaignPrefs prefs;public List<Voxels.TowerDefense.HeroDefinition> heroes;public List<int> oldDeployOrder=new List<int>();}
 public class CampaignSaveMeta {public bool gameOver;public int saveSlot,campaignNumber=1,playTime;public float campaignFraction;public DateTime savedTime;public Voxels.TowerDefense.CampaignPrefs prefs;public void SetSaveSlot(int slot){saveSlot=slot;}}
}
namespace Voxels.TowerDefense.CampaignGeneration {public class HeroGeneratorUI : UnityEngine.Object {
 public static HeroGeneratorUI instance=new HeroGeneratorUI();public UnityEngine.Gradient gradient=new UnityEngine.Gradient();public UnityEngine.GameObject swapSource=new UnityEngine.GameObject();public enum ChangeFlags{All}public static int appearances;
 public Voxels.TowerDefense.MonoHero GetStartingMonoHero(){return new Voxels.TowerDefense.MonoHero();}
 public void RandomizeStartingHero(Voxels.TowerDefense.HeroDefinition h,List<Voxels.TowerDefense.SerializableHeroUpgrade> u,bool deluxe,ref int seed,int step,ChangeFlags flags,List<Voxels.TowerDefense.HeroDefinition> others){if(others.Count!=0)throw new Exception("Four-player hue loop regression");appearances++;seed++;}
}}
namespace Voxels.TowerDefense.Upgrades {
 public class ReplenishAbility : UnityEngine.Object {public bool available=true;public List<Voxels.TowerDefense.SquadReplenishLocation> locations=new List<Voxels.TowerDefense.SquadReplenishLocation>();public bool IsAvailable(){return available;}public static int heals;public void SelectLocation(Voxels.TowerDefense.SquadReplenishLocation x){heals++;}}
 public class EvacuateAbility : UnityEngine.Object {public bool available=true;public List<Voxels.TowerDefense.SquadEvacuationLocation> locations=new List<Voxels.TowerDefense.SquadEvacuationLocation>();public bool IsAvailable(){return available;}public static int evacuations;private void SelectLocation(Voxels.TowerDefense.SquadEvacuationLocation x){evacuations++;}}
}
namespace Voxels.TowerDefense.UI {public class MainMenu{} public static class MetaMenuHelpers{public static int newGames;public static void NewGame(Voxels.TowerDefense.ProfileInternals.CampaignSave s){newGames++;Voxels.TowerDefense.Singleton<Voxels.TowerDefense.CampaignManager>.instance.campaign=new UnityEngine.Object();}}}
namespace BadNorthMP.UI {public static class MultiplayerScreen {public static int opens;public static bool Open(){opens++;return true;}public static void RequestOpen(){opens++;}public static void CloseForBattle(){}}public static class GameNotify {public static string last;public static void Post(string s){last=s;}}}
namespace BadNorthMP.Spectator {public static class SpectatorSession{public static bool Active;public static bool IslandReady(){return true;}}}
namespace BadNorthMP.Game {
 public static class Evacuation {public static bool Order(Voxels.TowerDefense.EnglishSquad squad,Voxels.TowerDefense.SquadEvacuationLocation boat,out string result){var ability=squad.upgradeManager.GetUpgrade<Voxels.TowerDefense.Upgrades.EvacuateAbility>();result="No boat is beached and empty yet.";if(ability==null)return false;
  foreach(var location in ability.locations)if(location.IsAvailable()){typeof(Voxels.TowerDefense.Upgrades.EvacuateAbility).GetMethod("SelectLocation",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(ability,new object[]{location});result="Fleeing to a boat.";return true;}return false;}}
 public static class MultiplayerCampaigns {
 public const int FirstSlot=100,SlotCount=5;public static int FreeSlot=100;public static bool PendingNew;public static Voxels.TowerDefense.ProfileInternals.CampaignSaveMeta[] Saves=new Voxels.TowerDefense.ProfileInternals.CampaignSaveMeta[5];
 public static int Deleted=-1;public static void Delete(Voxels.TowerDefense.ProfileInternals.CampaignSaveMeta m,Action done=null){Deleted=m.saveSlot;Saves[m.saveSlot-FirstSlot]=null;if(done!=null)done();}
 public static IEnumerator LoadHeaders(){yield break;}public static void Continue(Voxels.TowerDefense.ProfileInternals.CampaignSaveMeta m){Voxels.TowerDefense.Profile.activeCampaignMeta=m;Voxels.TowerDefense.Profile.campaign=new Voxels.TowerDefense.ProfileInternals.CampaignSave{seed=99,heroes=new List<Voxels.TowerDefense.HeroDefinition>()};for(int i=0;i<10;i++)Voxels.TowerDefense.Profile.campaign.heroes.Add(new Voxels.TowerDefense.HeroDefinition(i){recruited=i<2});}public static void Remember(Voxels.TowerDefense.ProfileInternals.CampaignSaveMeta m){}
}}
namespace BadNorthMP {
 public static class ModLog {public static void Warn(string s){}public static void Info(string s){}}
 public static class FabricWrapper{public static void PostEvent(string s){}}
 public static class GameAccess{public static string Describe(object o){return "test";}public static bool IsPlaying=true;public static Voxels.TowerDefense.Island CurrentIsland=new Voxels.TowerDefense.Island();public static List<Voxels.TowerDefense.Squad> PlayerSquads=new List<Voxels.TowerDefense.Squad>();}
}
namespace BadNorthMP.Net {
 internal static class NativeDefenderControls { public static int movesShown,restores; public static void UpdateTiles(){} public static void Report(){} public static void RestoreTiles(){restores++;} public static void MoveSent(Voxels.TowerDefense.EnglishSquad s,Voxels.TowerDefense.NavSpot n,bool deselect){movesShown++;} public static bool Execute(Voxels.TowerDefense.EnglishSquad squad,BinaryReader reader,out string result){result="Native ability mock";return true;} }
 internal class SteamSession : UnityEngine.Object {
 public static SteamSession Instance=new SteamSession();public Steamworks.CSteamID Me=new Steamworks.CSteamID(1),Owner=new Steamworks.CSteamID(1),Lobby=new Steamworks.CSteamID(100);
 public bool InLobby {get{return Lobby!=Steamworks.CSteamID.Nil;}}public bool IsHost{get{return Me==Owner;}}public List<Steamworks.CSteamID> members=new List<Steamworks.CSteamID>();public List<Sent> sent=new List<Sent>();
 public class Sent{public Steamworks.CSteamID to;public byte[] data;}public List<Steamworks.CSteamID> Members(){return new List<Steamworks.CSteamID>(members);}
 public List<Steamworks.CSteamID> resets=new List<Steamworks.CSteamID>();public event Action<Steamworks.CSteamID> ConnectionReset=delegate{};public void ResetConnection(Steamworks.CSteamID u){resets.Add(u);ConnectionReset(u);}
 public void Send(Steamworks.CSteamID to,byte[] data){if(to!=Me)sent.Add(new Sent{to=to,data=data});}public void Broadcast(byte[] data){foreach(var p in members)Send(p,data);}public void Leave(string s){Lobby=Steamworks.CSteamID.Nil;}
 public static string NameOf(Steamworks.CSteamID p){return p.m_SteamID.ToString();}
 }
 internal static class Updater{public static bool Compatible=true;}
 internal static class Router{public delegate void Handler(Steamworks.CSteamID p,BinaryReader r);public static Dictionary<MsgType,Handler> handlers=new Dictionary<MsgType,Handler>();public static void On(MsgType t,Handler h){handlers[t]=h;}
 public static void Receive(ulong sender,byte[] data){using(var r=new BinaryReader(new MemoryStream(data))){var t=(MsgType)r.ReadByte();handlers[t](new Steamworks.CSteamID(sender),r);}}}
 internal class MultiplayerGame : UnityEngine.MonoBehaviour {public static MultiplayerGame Instance=new MultiplayerGame();}
 internal static class CampaignSync{public static void MarkDirty(){}}
 public static class PartyTests {
 static int checks;static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
 static Steamworks.CSteamID Id(ulong n){return new Steamworks.CSteamID(n);}
 static void Reset(int count){var s=SteamSession.Instance;s.Me=Id(1);s.Owner=Id(1);s.Lobby=Id(s.Lobby.m_SteamID+1);s.members.Clear();for(ulong i=1;i<=(ulong)count+1;i++)s.members.Add(Id(i));s.sent.Clear();PartyFlow.Update();Voxels.TowerDefense.Singleton<Voxels.TowerDefense.CampaignManager>.instance.campaign=null;Voxels.TowerDefense.Singleton<Voxels.TowerDefense.Stack>.instance.stateLoading.SetActive(false);Voxels.TowerDefense.Profile.campaign=null;BadNorthMP.Game.MultiplayerCampaigns.FreeSlot=100;}
 static byte[] Pick(ulong p,int token,int[] types,bool ready){return Packet.Build(MsgType.PartyPick,w=>{w.Write(token);w.Write((byte)types.Length);foreach(int t in types)w.Write((byte)t);w.Write(ready);});}
 static byte[] Last(MsgType t){var s=SteamSession.Instance;for(int i=s.sent.Count-1;i>=0;i--)if(s.sent[i].data[0]==(byte)t)return s.sent[i].data;throw new Exception("No sent "+t);}
 static byte[] Order(int token,int round,int hero,int target,bool action){return Packet.Build(action?MsgType.DefenderAction:MsgType.DefenderMove,w=>{w.Write(token);w.Write(round);w.Write(Voxels.TowerDefense.Profile.campaign.seed);w.Write(BadNorthMP.GameAccess.CurrentIsland.levelNode.index);w.Write(hero);w.Write(target);});}
 public static void Run(){
 Directory.CreateDirectory(BepInEx.Paths.ConfigPath);
 try {Core();Network();Recruits();Loadouts();Crashes();Reconnect();Deletes();Console.WriteLine(checks+" party/draft/ownership/order checks passed against actual source (game and Steam mocked).");}
 finally{string path=Path.GetFullPath(BepInEx.Paths.ConfigPath);if(!path.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()),"BNMP-party-test-"),StringComparison.OrdinalIgnoreCase))throw new Exception("Unsafe test cleanup target");Directory.Delete(path,true);}
 }
 // A guest who hears nothing from the host during the draft drops the session and asks for the state again; the host answers.
 static void Reconnect(){
 var s=SteamSession.Instance;Reset(2);PartyFlow.StartDraft();var hostState=Last(MsgType.PartyState);
 s.Me=Id(2);UnityEngine.Time.unscaledTime+=1;Router.Receive(1,hostState);Check(PartyFlow.State.phase==1,"guest missed the draft state");
 s.sent.Clear();s.resets.Clear();UnityEngine.Time.unscaledTime+=5;PartyFlow.Update();Check(s.resets.Count==0,"guest reconnected while the host was still talking");
 UnityEngine.Time.unscaledTime+=4;PartyFlow.Update();Check(s.resets.Count==1&&s.resets[0]==Id(1)&&Last(MsgType.PartyStateRequest).Length==1,"guest didn't reconnect after 8 s of silence");
 PartyFlow.Update();Check(s.resets.Count==1,"guest reconnected every frame");
 Router.Receive(1,hostState);UnityEngine.Time.unscaledTime+=9;PartyFlow.Update();Check(s.resets.Count==2,"guest stopped recovering after a second silence");
 s.Me=Id(1);s.sent.Clear();Router.Receive(2,Packet.Build(MsgType.PartyStateRequest,w=>{}));Check(s.sent.Exists(m=>m.data[0]==(byte)MsgType.PartyState),"host didn't answer a state request");
 s.sent.Clear();s.ResetConnection(Id(2));Check(s.sent.Exists(m=>m.data[0]==(byte)MsgType.PartyState),"host didn't resend the state after a connection reset");
 }
 // The host can delete a multiplayer campaign; only the campaign leader (whose saves they are) does it, and only when the host asks.
 static void Deletes(){
 var s=SteamSession.Instance;Reset(1);
 var meta=new Voxels.TowerDefense.ProfileInternals.CampaignSaveMeta();meta.saveSlot=101;BadNorthMP.Game.MultiplayerCampaigns.Saves[1]=meta;BadNorthMP.Game.MultiplayerCampaigns.Deleted=-1;
 s.Me=Id(1);s.sent.Clear();PartyFlow.RequestDelete(1);
 Check(s.sent.Count==1&&s.sent[0].to==Id(2)&&s.sent[0].data[0]==(byte)MsgType.PartyDeleteSave&&!PartyFlow.Occupied[1],"delete request not sent to the campaign leader");
 var request=s.sent[0].data;
 s.Me=Id(2);Router.Receive(2,request);Check(BadNorthMP.Game.MultiplayerCampaigns.Deleted==-1,"someone other than the host deleted a campaign");
 Router.Receive(1,Packet.Build(MsgType.PartyDeleteSave,w=>w.Write(7)));Check(BadNorthMP.Game.MultiplayerCampaigns.Deleted==-1,"an invalid slot was deleted");
 typeof(PartyFlow).GetField("catalogSent",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,true);
 Router.Receive(1,request);Check(BadNorthMP.Game.MultiplayerCampaigns.Deleted==101&&BadNorthMP.Game.MultiplayerCampaigns.Saves[1]==null,"the leader didn't delete at the host's request");
 Check(!(bool)typeof(PartyFlow).GetField("catalogSent",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null),"the leader didn't resend its save list after deleting");
 BadNorthMP.Game.MultiplayerCampaigns.Deleted=-1;Router.Receive(1,request);Check(BadNorthMP.Game.MultiplayerCampaigns.Deleted==-1,"an empty slot was deleted again");
 s.Me=Id(1);
 }
 static void Crashes(){
 var old=Voxels.TowerDefense.Profile.userSave;Voxels.TowerDefense.Profile.userSave=null;
 Check(PartyLoadout.Catalog(true).Length==0&&PartyLoadout.Catalog(false).Length==0,"fresh profile catalogue crashed");
 Voxels.TowerDefense.Profile.userSave=new Voxels.TowerDefense.TestUser{inventory=null};Check(PartyLoadout.Catalog(false).Length==0,"null inventory catalogue crashed");
 Voxels.TowerDefense.Profile.userSave=new Voxels.TowerDefense.TestUser();Voxels.TowerDefense.Profile.userSave.inventory.startingItems=null;Check(PartyLoadout.Catalog(false).Length==0,"null starting item list crashed");
 var broken=new Voxels.TowerDefense.HeroUpgradeDefinition{name="MissingType",upgradeType=null};Voxels.TowerDefense.Profile.userSave.inventory.startingTraits.Add(broken);Check(PartyLoadout.Catalog(true).Length==0,"broken definition crashed catalogue");
 var grail=new Voxels.TowerDefense.HeroUpgradeDefinition{name="Grail",typeEnum=Voxels.TowerDefense.HeroUpgradeTypeEnum.Consumable};UnityEngine.Resources.loaded=new object[]{grail};
 Reset(1);PartyFlow.StartDraft();int purchases=Voxels.TowerDefense.HeroDefinition.setups,atlases=SpriteComposing.SpriteComposer.created;
 for(int i=0;i<40;i++){var hero=PartyFlow.CreateStartingHero(0,i%PartyRoster.ChoiceCount,"","Grail");Check(hero.itemUpgrade.definition==grail,"Grail preview dropped definition");}
 Check(Voxels.TowerDefense.HeroDefinition.setups==purchases,"preview ran purchase effects");Check(SpriteComposing.SpriteComposer.created==atlases+40,"portrait rebuilds reused global atlas");
 Voxels.TowerDefense.Profile.campaign=new Voxels.TowerDefense.ProfileInternals.CampaignSave();var starter=PartyFlow.CreateStartingHero(0,0,"","Grail");PartyLoadout.Commit(starter);
 Check(starter.itemUpgrade==null&&Voxels.TowerDefense.Profile.campaign.inventory.Contains(grail),"Grail purchase ran before inventory transfer");
 Reset(1);SteamSession.Instance.Me=Id(2);Voxels.TowerDefense.Profile.userSave=null;PartyFlow.Update();
 Check(!(bool)typeof(PartyFlow).GetField("headersLoading",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null),"catalogue failure locked headersLoading");
 Voxels.TowerDefense.Profile.userSave=new Voxels.TowerDefense.TestUser();int sent=SteamSession.Instance.sent.Count;for(int i=0;i<30;i++)PartyFlow.Update();Check(SteamSession.Instance.sent.Count==sent,"catalogue retried every frame");
 UnityEngine.Time.unscaledTime+=4;PartyFlow.Update();Check(Last(MsgType.PartyCatalog).Length>0,"empty catalogue was not sent after profile ready");SteamSession.Instance.Me=Id(1);Router.Receive(2,Last(MsgType.PartyCatalog));Check(PartyFlow.CatalogReady&&!PartyFlow.Saved[0],"no saves prevented catalogue completion");
 Voxels.TowerDefense.Profile.userSave=old;UnityEngine.Resources.loaded=new object[0];
 }
 static void Loadouts(){
 var heavy=new Voxels.TowerDefense.HeroUpgradeDefinition{name="HeavyWeapons",typeEnum=Voxels.TowerDefense.HeroUpgradeTypeEnum.Trait};
 var item=new Voxels.TowerDefense.HeroUpgradeDefinition{name="Ring",typeEnum=Voxels.TowerDefense.HeroUpgradeTypeEnum.Item};
 var consumable=new Voxels.TowerDefense.HeroUpgradeDefinition{name="Flask",typeEnum=Voxels.TowerDefense.HeroUpgradeTypeEnum.Consumable};
 var invalid=new Voxels.TowerDefense.HeroUpgradeDefinition{name="Archer",typeEnum=Voxels.TowerDefense.HeroUpgradeTypeEnum.Class};
 UnityEngine.Resources.loaded=new object[]{heavy,item,consumable,invalid};
 Voxels.TowerDefense.Profile.userSave.inventory.startingTraits=new List<Voxels.TowerDefense.HeroUpgradeDefinition>{heavy,invalid};
 Voxels.TowerDefense.Profile.userSave.inventory.startingItems=new List<Voxels.TowerDefense.HeroUpgradeDefinition>{item,consumable,invalid};
 Check(PartyLoadout.Catalog(true).Length==1,"starting class exposed as trait");Check(PartyLoadout.Catalog(false).Length==2,"starting item/consumable missing");
 var roster=new PartyRoster{allowedTraits=new string[]{"HeavyWeapons"},allowedItems=new string[]{"Ring","Flask"}};roster.Draft(new ulong[]{2,3});
 Check(roster.Pick(2,roster.token,new int[]{1},true,new string[]{"HeavyWeapons"},new string[]{"Ring"}),"trait pick refused");
 Check(!roster.Pick(3,roster.token,new int[]{1},true,new string[]{"Archer"},new string[]{"Ring"}),"class accepted in starting loadout");
 Check(!roster.Find(3).ready,"invalid selection marked ready");
 Check(!roster.Pick(3,roster.token,new int[]{1},true,new string[]{"HeavyWeapons"},new string[0]),"mismatched loadout slots accepted");
 using(var stream=new MemoryStream()){var w=new BinaryWriter(stream);roster.Write(w);w.Flush();stream.Position=0;var copy=PartyRoster.Read(new BinaryReader(stream));Check(copy.Find(2).traits[0]=="HeavyWeapons"&&copy.Find(2).items[0]=="Ring","loadout lost on wire");}
 roster.Draft(new ulong[]{2});Check(roster.Find(2).traits[0]=="HeavyWeapons"&&!roster.Find(2).ready,"redraft lost trait or kept ready");
 Reset(2);PartyFlow.StartDraft();PartyFlow.State.allowedTraits=new string[]{"HeavyWeapons"};PartyFlow.State.allowedItems=new string[]{"Ring","Flask"};
 var hero=PartyFlow.CreateStartingHero(0,1,"HeavyWeapons","Ring");Check(hero.classUpgrade==null&&hero.traitUpgrade.definition==heavy&&hero.itemUpgrade.definition==item,"militia lost starting trait/item");
 hero=PartyFlow.CreateStartingHero(1,1,"HeavyWeapons","Flask");Voxels.TowerDefense.Profile.campaign=new Voxels.TowerDefense.ProfileInternals.CampaignSave();PartyLoadout.TransferConsumable(hero);
 Check(hero.itemUpgrade==null&&hero.traitUpgrade.definition==heavy&&Voxels.TowerDefense.Profile.campaign.inventory.Contains(consumable),"consumable equipped or trait dropped");
 bool rejected=false;try{PartyLoadout.Resolve("Archer",true);}catch(IOException){rejected=true;}Check(rejected,"class resolved as starting trait");
 SteamSession.Instance.Me=Id(2);PartyFlow.CycleUpgrade(0,true,1);Check(PartyFlow.Mine.traits[0]=="HeavyWeapons","trait arrow skipped first choice");PartyFlow.CycleUpgrade(0,true,1);Check(PartyFlow.Mine.traits[0]=="","trait arrow cannot select none");
 SteamSession.Instance.Me=Id(1);Router.Receive(2,Pick(2,PartyFlow.State.token,new int[]{1},true));Router.Receive(3,Pick(3,PartyFlow.State.token,new int[]{1},true));PartyFlow.Update();
 var begin=Last(MsgType.PartyBegin);var state=Last(MsgType.PartyState);int count=SteamSession.Instance.sent.Count;UnityEngine.Time.unscaledTime+=3;PartyFlow.Update();Check(SteamSession.Instance.sent.Count>count,"loading start not retried");
 int created=Voxels.TowerDefense.Profile.creates;SteamSession.Instance.Me=Id(2);PartyFlow.State=new PartyRoster{token=PartyFlow.State.token};Router.Receive(1,begin);
 Check(Voxels.TowerDefense.Profile.creates==created+1,"begin failed when preceding role state was delayed");Router.Receive(1,state);Router.Receive(1,begin);Check(Voxels.TowerDefense.Profile.creates==created+1,"retry created duplicate campaign");
 Reset(1);PartyFlow.StartDraft();var stale=Packet.Build(MsgType.PartyState,w=>{PartyFlow.State.Write(w);w.Write("old state");});SteamSession.Instance.Me=Id(2);
 PartyFlow.ChooseCommander(0,7);Check(PartyFlow.PendingPick&&PartyFlow.Mine.choices[0]==7,"portrait choice not saved optimistically");Router.Receive(1,stale);
 Check(PartyFlow.PendingPick&&PartyFlow.Mine.choices[0]==7,"stale host echo erased the last portrait pick");int sent=SteamSession.Instance.sent.Count;PartyFlow.Ready();Check(sent==SteamSession.Instance.sent.Count,"Ready raced unconfirmed selection");
 var ack=Packet.Build(MsgType.PartyState,w=>{PartyFlow.State.Write(w);w.Write("ack");});Router.Receive(1,ack);Check(!PartyFlow.PendingPick,"matching host ack did not unlock Ready");PartyFlow.Ready();Check(PartyFlow.PendingPick&&PartyFlow.Mine.ready,"Ready not pending host confirmation");
 // Continuing a save: the party state says so, and picks (portraits, traits, items) are ignored; Ready still works.
 Reset(1);PartyFlow.SaveChoice=2;PartyFlow.StartDraft();PartyFlow.SaveChoice=-1;Check(PartyFlow.State.Continuing&&PartyFlow.State.continuing==2&&Copy(PartyFlow.State).continuing==2,"continued save not marked, or lost on the wire");
 SteamSession.Instance.Me=Id(2);int sentBefore=SteamSession.Instance.sent.Count;PartyFlow.ChooseCommander(0,5);PartyFlow.CycleUpgrade(0,true,1);
 Check(SteamSession.Instance.sent.Count==sentBefore&&PartyFlow.Mine.choices[0]!=5&&PartyFlow.Mine.traits[0]=="","picks changed a continued campaign");
 PartyFlow.Ready();Check(PartyFlow.PendingPick&&PartyFlow.Mine.ready,"Ready doesn't work when continuing");
 var badSave=Copy(PartyFlow.State);badSave.continuing=7;bool refusedSave=false;try{Copy(badSave);}catch(IOException){refusedSave=true;}Check(refusedSave,"an invalid continued save was accepted");
 UnityEngine.Resources.loaded=new object[0];
 }
 static void Core(){
 for(int count=1;count<=4;count++){
 var roster=new PartyRoster();var players=new List<ulong>();for(ulong p=2;p<(ulong)count+2;p++)players.Add(p);roster.Draft(players);
 Check(roster.seats.Count==count,"wrong party size");Check(!roster.AllReady,"draft starts ready");
 foreach(var seat in roster.seats){Check(seat.choices.Length==(count==1?2:1),"wrong squad scaling");Check(!roster.Pick(seat.player,roster.token-1,seat.choices,true),"stale draft accepted");Check(!roster.Pick(seat.player,roster.token,new int[3],true),"excess squads accepted");Check(!roster.Pick(seat.player,roster.token,new int[]{PartyRoster.ChoiceCount},true),"invalid commander accepted");Check(roster.Pick(seat.player,roster.token,seat.choices,true),"valid pick rejected");}
 Check(roster.AllReady,"party never ready");roster.AssignStartingHeroes();roster.phase=2;
 Check(!roster.Pick(2,roster.token,roster.seats[0].choices,false),"draft mutable after launch");
 var seen=new HashSet<int>();foreach(var seat in roster.seats)foreach(int hero in seat.heroes){Check(seen.Add(hero),"shared commander");Check(roster.Owns(seat.player,hero),"owner cannot control");Check(!roster.Owns(1,hero),"host controls defenders");foreach(ulong other in players)if(other!=seat.player)Check(!roster.Owns(other,hero),"cross-owner command");}
 using(var stream=new MemoryStream()){using(var w=new BinaryWriter(stream)){roster.Write(w);w.Flush();stream.Position=0;var copy=PartyRoster.Read(new BinaryReader(stream));Check(copy.seats.Count==count&&copy.token==roster.token,"party wire roundtrip");foreach(var seat in copy.seats)foreach(int hero in seat.heroes)Check(copy.Owns(seat.player,hero),"ownership lost on wire");}}
 }
 var grow=new PartyRoster();grow.Draft(new ulong[]{2});grow.Pick(2,grow.token,new int[]{1,2},true);int token=grow.token;grow.Draft(new ulong[]{2,3});Check(grow.token>token&&grow.seats[0].choices.Length==1&&!grow.AllReady,"join failed to invalidate solo ready");Check(grow.seats[0].choices[0]==1,"class preference discarded");grow.Draft(new ulong[]{2});Check(grow.seats[0].choices.Length==2&&!grow.AllReady,"leave failed to restore solo slots");
 }
 static PartyRoster Copy(PartyRoster roster){using(var stream=new MemoryStream()){var w=new BinaryWriter(stream);roster.Write(w);w.Flush();stream.Position=0;return PartyRoster.Read(new BinaryReader(stream));}}
 static void Party(int defenders){Reset(defenders);PartyFlow.StartDraft();foreach(var seat in PartyFlow.State.seats)Router.Receive(seat.player,Pick(seat.player,PartyFlow.State.token,seat.choices,true));PartyFlow.Update();var begin=Last(MsgType.PartyBegin);var state=Last(MsgType.PartyState);SteamSession.Instance.Me=Id(2);Router.Receive(1,state);Router.Receive(1,begin);PartyFlow.CampaignReady();
  var heroes=Voxels.TowerDefense.Profile.campaign.heroes;for(int i=heroes.Count;i<10;i++)heroes.Add(new Voxels.TowerDefense.HeroDefinition(i));}
 static List<Voxels.TowerDefense.HeroDefinition> Deploy(Voxels.TowerDefense.HeroDefinition rescue){var manager=new Voxels.TowerDefense.IslandGameplayManager();manager.states.running.active=false;manager.island.levelNode.heroDefinition=rescue;typeof(PartyDeployPatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{manager});return Voxels.TowerDefense.SquadSpawner.deployed;}
 static int Count(List<Voxels.TowerDefense.HeroDefinition> list,ulong player){int n=0;foreach(var h in list)if(PartyFlow.State.Owns(player,Voxels.TowerDefense.Profile.campaign.heroes.IndexOf(h)))n++;return n;}
 static void Recruits(){
 // Rescued commanders go round the table: leader (2), then 3, 4, 5; disconnected players are skipped.
 Party(4);var heroes=Voxels.TowerDefense.Profile.campaign.heroes;
 Check(heroes.Count==10&&!heroes[4].recruited,"rescue pool missing");
 var deployed=Deploy(heroes[4]);
 Check(PartyFlow.State.Owns(2,4),"first rescue did not go to the campaign leader");
 Check(deployed.Count==4&&deployed[3]==heroes[4],"rescue not deployed last within four squads");
 Check(Count(deployed,2)==1&&Count(deployed,3)==1&&Count(deployed,4)==1&&Count(deployed,5)==1,"a player lost their squad to the rescue");
 Check(Last(MsgType.PartyOwnership).Length>0&&PartyFlow.State.round==1,"rescue owner not published before spawning");
 Deploy(heroes[4]);Check(PartyFlow.State.Owns(2,4)&&PartyFlow.State.nextRecruit==1,"retrying the island moved the rescue");
 heroes[4].recruited=true;
 Deploy(heroes[5]);Check(PartyFlow.State.Owns(3,5),"second rescue skipped player 1");heroes[5].recruited=true;
 SteamSession.Instance.members.Remove(Id(4));Deploy(heroes[6]);Check(PartyFlow.State.Owns(5,6),"rescue given to a disconnected player");heroes[6].recruited=true;SteamSession.Instance.members.Add(Id(4));
 Deploy(heroes[7]);Check(PartyFlow.State.Owns(2,7),"rescue order did not wrap to the leader");
 Check(PartyFlow.State.Owns(2,7)&&!PartyFlow.State.Owns(4,7),"rescue ownership shared");
 // A disconnected owner's unrecruited rescue moves on; a recruited one stays.
 SteamSession.Instance.members.Remove(Id(2));Deploy(heroes[7]);Check(!PartyFlow.State.Owns(2,7)&&PartyFlow.State.OwnerOf(7)!=null,"absent player's pending rescue stuck");SteamSession.Instance.members.Add(Id(2));
 // Recruits survive the wire and ownership checks cover them.
 var copy=Copy(PartyFlow.State);Check(copy.Owns(3,5)&&copy.Owns(5,6)&&copy.nextRecruit==PartyFlow.State.nextRecruit,"recruits lost on the wire");
 PartyFlow.State.seats[1].recruits.Add(0);bool rejected=false;try{Copy(PartyFlow.State);}catch(IOException){rejected=true;}Check(rejected,"commander owned twice accepted");PartyFlow.State.seats[1].recruits.Remove(0);
 // Ownership file keeps recruits and the turn order; unowned recruited commanders join in turn.
 var store=typeof(PartyFlow).GetMethod("StoreOwnership",BindingFlags.NonPublic|BindingFlags.Static);var restore=typeof(PartyFlow).GetMethod("RestoreOwnership",BindingFlags.NonPublic|BindingFlags.Static);
 int next=PartyFlow.State.nextRecruit;var owner7=PartyFlow.State.OwnerOf(7).player;store.Invoke(null,null);
 heroes[8].recruited=true;restore.Invoke(null,null);
 Check(PartyFlow.State.Owns(2,4)&&PartyFlow.State.Owns(3,5)&&PartyFlow.State.Owns(5,6)&&PartyFlow.State.Owns(owner7,7),"recruits lost on continue");
 Check(PartyFlow.State.OwnerOf(8)!=null&&PartyFlow.State.OwnerOf(8)==PartyFlow.State.seats[next],"recruited commander without owner not assigned in turn");
 // Four-squad cap: one each first, spare slots rotate, longest-waiting commander first, rescue needs company.
 Party(1);heroes=Voxels.TowerDefense.Profile.campaign.heroes;for(int i=2;i<6;i++){heroes[i].recruited=true;PartyFlow.State.seats[0].recruits.Add(i);}
 Check(PartyFlow.Deployment(null).Count==4,"solo party exceeded four squads");
 Party(3);heroes=Voxels.TowerDefense.Profile.campaign.heroes;for(int i=3;i<6;i++){heroes[i].recruited=true;PartyFlow.State.seats[i-3].recruits.Add(i);}
 PartyFlow.State.round=0;var squads=PartyFlow.Deployment(null);Check(squads.Count==4&&Count(squads,2)==2&&Count(squads,3)==1&&Count(squads,4)==1,"spare slot not given in seat order");
 PartyFlow.State.round=1;squads=PartyFlow.Deployment(null);Check(Count(squads,3)==2&&Count(squads,2)==1,"spare slot did not rotate");
 PartyFlow.State.round=0;Voxels.TowerDefense.Profile.campaign.oldDeployOrder.Add(0);squads=PartyFlow.Deployment(null);Check(squads.IndexOf(heroes[3])==0,"rested commander did not get priority");
 Party(2);heroes=Voxels.TowerDefense.Profile.campaign.heroes;heroes[0].fatigued=true;heroes[1].fatigued=true;
 Check(PartyFlow.Deployment(Rescue(heroes[2])).Count==0,"rescued commander deployed alone");
 }
 static Voxels.TowerDefense.Island Rescue(Voxels.TowerDefense.HeroDefinition hero){var island=new Voxels.TowerDefense.Island();island.levelNode.heroDefinition=hero;return island;}
 static void Network(){
 PartyFlow.Init();RemoteDefender.Init();
 Check(new Voxels.TowerDefense.ProfileInternals.CampaignSave().heroes==null,"mock must reproduce native null commander list");
 Reset(1);PartyFlow.StartDraft();int released=Voxels.TowerDefense.MonoHero.destroyed;
 Voxels.TowerDefense.MonoHero.FailSetup=true;
 for(int attempt=0;attempt<10;attempt++){
 bool failed=false;try{PartyFlow.CreateStartingHero(0,0);}catch(InvalidOperationException){failed=true;}
 Check(failed,"portrait failure swallowed");
 }
 Voxels.TowerDefense.MonoHero.FailSetup=false;
 Check(Voxels.TowerDefense.MonoHero.destroyed==released+10,"failed portrait retries leaked temporary heroes");
 var defs=new Voxels.TowerDefense.HeroClassUpgradeDefinition[3];for(int n=0;n<3;n++)defs[n]=new Voxels.TowerDefense.HeroClassUpgradeDefinition{minionAgentPrefab=new Voxels.TowerDefense.Agent{kind=n==1?(object)new Voxels.TowerDefense.Archery():n==2?(object)new Voxels.TowerDefense.Spear():new object()}};UnityEngine.Resources.loaded=defs;
 Reset(2);SteamSession.Instance.Me=Id(2);PartyFlow.StartDraft();Check(!PartyFlow.Active,"defender started campaign");SteamSession.Instance.Me=Id(1);PartyFlow.StartDraft();Check(PartyFlow.State.phase==1&&PartyFlow.State.seats.Count==2,"host start failed");int token=PartyFlow.State.token;
 Router.Receive(1,Pick(1,token,new int[]{1},true));Check(!PartyFlow.State.AllReady,"mastermind picked defender unit");Router.Receive(2,Pick(2,token,new int[]{1},true));PartyFlow.Update();Check(PartyFlow.State.phase==1,"campaign loaded before every defender ready");Router.Receive(3,Pick(3,token,new int[]{2},true));PartyFlow.Update();Check(PartyFlow.Loading,"all ready didn't start");var begin=Last(MsgType.PartyBegin);var state=Last(MsgType.PartyState);
 SteamSession.Instance.Me=Id(2);Router.Receive(1,state);int created=Voxels.TowerDefense.Profile.creates;Router.Receive(1,begin);Check(Voxels.TowerDefense.Profile.creates==created+1,"campaign not created once");Check(Voxels.TowerDefense.Profile.activeCampaignMeta.saveSlot>=100,"solo save overwritten");Check(Voxels.TowerDefense.Profile.campaign.heroes.Count==2,"new campaign squad count");Check(Voxels.TowerDefense.Profile.campaign.heroes[0].classUpgrade==null&&Voxels.TowerDefense.Profile.campaign.heroes[1].classUpgrade==null,"starter received a class upgrade");Router.Receive(1,begin);Check(Voxels.TowerDefense.Profile.creates==created+1,"duplicate start created second campaign");
 PartyFlow.CampaignReady();Check(PartyFlow.Deployed&&PartyFlow.State.Owns(2,0)&&PartyFlow.State.Owns(3,1),"new campaign ownership wrong");var manager=new Voxels.TowerDefense.IslandGameplayManager();manager.states.running.active=false;typeof(PartyDeployPatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{manager});Check(Voxels.TowerDefense.SquadSpawner.deployed.Count==2&&PartyFlow.State.round==1,"party automatic deployment");manager.states.Spawning.active=true;
 typeof(PartyDeployPatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{manager});
 Check(PartyFlow.State.round==1,"duplicate EnterLevel started another deployment");
 manager.states.Spawning.active=false;
 var spawnHook=typeof(PartySpawnOwnershipPatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static);
 Check(!(bool)spawnHook.Invoke(null,null),"native loadout bypassed party ownership");PartyFlow.PreparingDeployment=true;
 Check((bool)spawnHook.Invoke(null,null),"owned party deployment blocked");PartyFlow.PreparingDeployment=false;
 Router.Receive(1,state);Check(PartyFlow.State.round==1&&PartyFlow.Deployed,"late loading state reset authority");
 var hero0=Voxels.TowerDefense.Profile.campaign.heroes[0];var hero1=Voxels.TowerDefense.Profile.campaign.heroes[1];var local=new Voxels.TowerDefense.EnglishSquad{hero=hero0};var remote=new Voxels.TowerDefense.EnglishSquad{hero=hero1};local.navSpotOccupant.enSquad=local;remote.navSpotOccupant.enSquad=remote;BadNorthMP.GameAccess.PlayerSquads.Clear();BadNorthMP.GameAccess.PlayerSquads.Add(local);BadNorthMP.GameAccess.PlayerSquads.Add(remote);var empty=new Voxels.TowerDefense.NavSpot();var occupied=new Voxels.TowerDefense.NavSpot{occupant=local.navSpotOccupant};BadNorthMP.GameAccess.CurrentIsland.navSpotter.navSpots.Clear();BadNorthMP.GameAccess.CurrentIsland.navSpotter.navSpots.Add(empty);BadNorthMP.GameAccess.CurrentIsland.navSpotter.navSpots.Add(occupied);
 Check(PartyFlow.CanSelect(local)&&!PartyFlow.CanSelect(remote),"local selection owns all squads");Check(!PartyFlow.CanOrder(remote,empty),"local controls teammate");
 var moveHook=typeof(BadNorthMP.Patches.MoveToDirectPatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static);
 Check(!(bool)moveHook.Invoke(null,new object[]{remote,empty}),"native move hook allowed teammate");Check((bool)moveHook.Invoke(null,new object[]{local,empty}),"native move hook refused owner");
 var selectHook=typeof(BadNorthMP.Patches.SelectSquadPatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static);
 Check(!(bool)selectHook.Invoke(null,new object[]{remote}),"native selection hook allowed teammate");Check((bool)selectHook.Invoke(null,new object[]{local}),"native selection hook refused owner");
int moved=Voxels.TowerDefense.SquadMover.moves;
 Router.Receive(3,Order(token,1,0,0,false));Check(Voxels.TowerDefense.SquadMover.moves==moved,"remote stole local commander");Router.Receive(3,Order(token,0,1,0,false));Check(Voxels.TowerDefense.SquadMover.moves==moved,"stale battle order accepted");Router.Receive(3,Order(token,1,1,1,false));Check(Voxels.TowerDefense.SquadMover.moves==moved,"remote swapped teammate");Router.Receive(3,Order(token,1,1,0,false));Check(Voxels.TowerDefense.SquadMover.moves==moved+1,"owned remote movement rejected");Check(!PartyFlow.ExecutingOrder,"remote execution scope leaked");
 Voxels.TowerDefense.Singleton<Voxels.TowerDefense.IslandGameplayManager>.instance.states.running.active=false;
 Router.Receive(3,Order(token,1,1,0,false));Check(Voxels.TowerDefense.SquadMover.moves==moved+1,"orders accepted after battle end");
 Voxels.TowerDefense.Singleton<Voxels.TowerDefense.IslandGameplayManager>.instance.states.running.active=true;

 var healing=new Voxels.TowerDefense.Upgrades.ReplenishAbility();healing.locations.Add(new Voxels.TowerDefense.SquadReplenishLocation());remote.upgradeManager.upgrades.Add(healing);Router.Receive(3,Order(token,1,1,0,true));Check(Voxels.TowerDefense.Upgrades.ReplenishAbility.heals==1,"remote cannot heal");Router.Receive(3,Order(token,1,0,0,true));Check(Voxels.TowerDefense.Upgrades.ReplenishAbility.heals==1,"remote healed someone else's unit");var evac=new Voxels.TowerDefense.Upgrades.EvacuateAbility();evac.locations.Add(new Voxels.TowerDefense.SquadEvacuationLocation());remote.upgradeManager.upgrades.Add(evac);Router.Receive(3,Order(token,1,1,1,true));Check(Voxels.TowerDefense.Upgrades.EvacuateAbility.evacuations==1,"remote cannot evacuate");
 SteamSession.Instance.Me=Id(3);BadNorthMP.Spectator.SpectatorSession.Active=true;
 int nativeMoves=Voxels.TowerDefense.SquadMover.moves;
 Check(!(bool)moveHook.Invoke(null,new object[]{remote,empty}),"remote native input executed a local movement");
 byte[] nativeOrder=Last(MsgType.DefenderMove);using(var nr=new BinaryReader(new MemoryStream(nativeOrder))){nr.ReadByte();Check(nr.ReadInt32()==token&&nr.ReadInt32()==1,"native remote order lost battle identity");nr.ReadInt32();nr.ReadInt32();Check(nr.ReadInt32()==1&&nr.ReadInt32()==0,"native remote order lost owned commander or tile");}
 Check(Voxels.TowerDefense.SquadMover.moves==nativeMoves,"remote native input moved puppet instead of forwarding");
 Check(!(bool)moveHook.Invoke(null,new object[]{local,empty}),"remote native input ordered a teammate");
 SteamSession.Instance.Me=Id(2);BadNorthMP.Spectator.SpectatorSession.Active=false;
 hero0.fatigued=true;Check(PartyFlow.Deployment(null).Count==1,"fatigued commander deployed");SteamSession.Instance.members.Remove(Id(3));Check(PartyFlow.Deployment(null).Count==0,"disconnected commander deployed");hero0.fatigued=false;SteamSession.Instance.members.Add(Id(3));Check(PartyFlow.Deployment(null).Count==2,"reconnect lost ownership");
 Reset(4);PartyFlow.StartDraft();foreach(var seat in PartyFlow.State.seats)Router.Receive(seat.player,Pick(seat.player,PartyFlow.State.token,seat.choices,true));PartyFlow.Update();begin=Last(MsgType.PartyBegin);state=Last(MsgType.PartyState);SteamSession.Instance.Me=Id(2);Router.Receive(1,state);Router.Receive(1,begin);Check(Voxels.TowerDefense.Profile.campaign.heroes.Count==4,"four defenders didn't receive four commanders");PartyFlow.CampaignReady();Check(PartyFlow.Deployment(null).Count==4,"four-player deployment truncated");
 Reset(1);PartyFlow.StartDraft();Check(PartyFlow.State.seats[0].choices.Length==2,"solo selection lacks second squad");Router.Receive(2,Pick(2,PartyFlow.State.token,new int[]{1,2},true));PartyFlow.Update();begin=Last(MsgType.PartyBegin);state=Last(MsgType.PartyState);SteamSession.Instance.Me=Id(2);Router.Receive(1,state);Router.Receive(1,begin);PartyFlow.CampaignReady();Check(PartyFlow.State.Owns(2,0)&&PartyFlow.State.Owns(2,1),"solo doesn't own both squads");
 Reset(2);PartyFlow.StartDraft();SteamSession.Instance.members.Add(Id(4));PartyFlow.Update();Check(PartyFlow.State.seats.Count==3&&!PartyFlow.State.AllReady,"draft join didn't resize");SteamSession.Instance.members.Remove(Id(2));PartyFlow.Update();Check(PartyFlow.State.authority==3,"draft leader not replaced before start");PartyFlow.CancelDraft();Check(!PartyFlow.Active,"host can't cancel draft");
 Reset(1);PartyFlow.StartDraft();Router.Receive(2,Pick(2,PartyFlow.State.token,new int[]{0,0},true));PartyFlow.Update();begin=Last(MsgType.PartyBegin);state=Last(MsgType.PartyState);SteamSession.Instance.Me=Id(2);Router.Receive(1,state);BadNorthMP.Game.MultiplayerCampaigns.FreeSlot=-1;created=Voxels.TowerDefense.Profile.creates;Router.Receive(1,begin);Check(Voxels.TowerDefense.Profile.creates==created,"full slots fell back to solo save");Check(Last(MsgType.PartyFailure).Length>1,"full save slots did not notify host");
 Reset(1);PartyFlow.NewSaveSlot=104;PartyFlow.AllowReplays=false;PartyFlow.StartDraft();Router.Receive(2,Pick(2,PartyFlow.State.token,new int[]{0,0},true));PartyFlow.Update();begin=Last(MsgType.PartyBegin);state=Last(MsgType.PartyState);SteamSession.Instance.Me=Id(2);Router.Receive(1,state);Router.Receive(1,begin);Check(Voxels.TowerDefense.Profile.activeCampaignMeta.saveSlot==104,"selected empty slot ignored");Check(!Voxels.TowerDefense.Profile.campaign.prefs.allowReplays,"restart setting lost in transit");
 Reset(1);PartyFlow.NewSaveSlot=104;PartyFlow.StartDraft();Router.Receive(2,Pick(2,PartyFlow.State.token,new int[]{0,0},true));PartyFlow.Update();begin=Last(MsgType.PartyBegin);state=Last(MsgType.PartyState);SteamSession.Instance.Me=Id(2);Router.Receive(1,state);BadNorthMP.Game.MultiplayerCampaigns.Saves[4]=new Voxels.TowerDefense.ProfileInternals.CampaignSaveMeta();created=Voxels.TowerDefense.Profile.creates;Router.Receive(1,begin);Check(Voxels.TowerDefense.Profile.creates==created,"occupied selected slot overwritten");BadNorthMP.Game.MultiplayerCampaigns.Saves[4]=null;
 }
 }
}
namespace BadNorthMP.Net {
 internal static class SelectionLifecycle { public static void Clear(){RemoteDefender.ForgetSelection();} }
}
