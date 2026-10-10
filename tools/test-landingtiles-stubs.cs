
namespace UnityEngine {
 public class Object {public static implicit operator bool(Object x){return x!=null;}public static void Destroy(Object x){}}
 public class Component:Object {public GameObject gameObject=new GameObject();}
 public class GameObject:Object {public Transform transform=new Transform();public GameObject(){}public GameObject(string name){}public void SetActive(bool value){}public T AddComponent<T>() where T:new(){return new T();}}
 public class Transform:Object {public void SetParent(Transform parent,bool stays){}}
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public float sqrMagnitude{get{return x*x+y*y+z*z;}}public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}}
 public static class Mathf {public static float Abs(float x){return System.Math.Abs(x);}}
}
namespace Voxels.TowerDefense {
 public struct NavPos {public bool valid;public UnityEngine.Vector3 wPos;}
 public class NavSpot:UnityEngine.Object {public int index;public NavPos navPos;}
 public class NavSpotter:UnityEngine.Object {public System.Collections.Generic.List<NavSpot> navSpots=new System.Collections.Generic.List<NavSpot>();}
 public class Longship:UnityEngine.Object {public float area;}
 public class Difficulty:UnityEngine.Object {public float shipSpeedMultiplier=1;}
 public class LevelNode {public System.Collections.Generic.List<Longship> possibleShips=new System.Collections.Generic.List<Longship>();public Difficulty diffiucltySettings;}
 public class Island:UnityEngine.Object {public Beaches beaches;public NavSpotter navSpotter=new NavSpotter();public LevelNode levelNode=new LevelNode();public UnityEngine.Transform runContainer=new UnityEngine.Transform();public object raid;}
 public class Beaches:UnityEngine.Object {public class Beach {public bool puddle;public struct Pos {public NavPos navPos;public UnityEngine.Vector3 dir;public Beach beach;}} public System.Collections.Generic.List<Beach.Pos> points=new System.Collections.Generic.List<Beach.Pos>();public System.Collections.Generic.List<Beach.Pos> GetBeachPositions(float spacing){return points;}}
}
namespace Voxels.TowerDefense.RaidGeneration {
 public class Wave {public object raid;public float waveStartTime,timeSpreadGroup,timeSpreadShip;public ShipGroup AddShipGroup(ShipGroup group){group.wave=this;return group;}}
 public class ShipGroup {public Wave wave;public Landing AddLanding(Landing landing){landing.shipGroup=this;return landing;}}
 public class Landing {public ShipGroup shipGroup;public float timeOffset;public Voxels.TowerDefense.Longship shipPrefab;public void Init(Voxels.TowerDefense.Island island){}public bool TryPlace(Voxels.TowerDefense.NavPos pos,UnityEngine.Vector3 dir,float speed,System.Collections.Generic.List<Landing> list){float time=shipGroup.wave.waveStartTime+timeOffset*shipGroup.wave.timeSpreadShip;return pos.valid && shipPrefab!=null && pos.wPos.z>=0;}}
}
namespace BadNorthMP.Game {
 public static class LandingTilesTest {
 public static void Run(){var island=new Voxels.TowerDefense.Island{beaches=new Voxels.TowerDefense.Beaches()};island.levelNode.possibleShips.Add(new Voxels.TowerDefense.Longship{area=1});island.navSpotter.navSpots.Add(new Voxels.TowerDefense.NavSpot{index=10,navPos=new Voxels.TowerDefense.NavPos{valid=true,wPos=new UnityEngine.Vector3(0,0,0)}});island.navSpotter.navSpots.Add(new Voxels.TowerDefense.NavSpot{index=20,navPos=new Voxels.TowerDefense.NavPos{valid=true,wPos=new UnityEngine.Vector3(2,0,0)}});
 var coast=new Voxels.TowerDefense.Beaches.Beach();foreach(var point in new[]{new UnityEngine.Vector3(0.5f,0,0),new UnityEngine.Vector3(0.7f,0,0),new UnityEngine.Vector3(2,1,0),new UnityEngine.Vector3(2,0,-0.5f)})island.beaches.points.Add(new Voxels.TowerDefense.Beaches.Beach.Pos{beach=coast,navPos=new Voxels.TowerDefense.NavPos{valid=true,wPos=point}});
 var choices=BeachChoices.Get(island);if(choices.Count!=1 || choices[0].tileIndex!=10 || choices[0].positions.Count!=1)throw new System.Exception("Expected only fitting, valid coastline at tile 10");if(choices[0].center.x!=0 || choices[0].positions[0].navPos.wPos.x!=0.5f)throw new System.Exception("Tile marker/shoreline mismatch");}
 }
}
