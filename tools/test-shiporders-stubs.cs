
namespace UnityEngine {public class MonoBehaviour { public static implicit operator bool(MonoBehaviour x){return x!=null;} }}
namespace Voxels.TowerDefense {
 public interface IAgentOrder {bool WantsControl();} public interface IBrainAction {}
 public class Brain:UnityEngine.MonoBehaviour {
  private System.Collections.Generic.List<IAgentOrder> orderList=new System.Collections.Generic.List<IAgentOrder>();
  public System.Collections.Generic.List<IBrainAction> actions=new System.Collections.Generic.List<IBrainAction>();public IAgentOrder order;public UnityEngine.MonoBehaviour orderMono;private static IAgentOrder nullOrder = null;
  public int OrderCount{get{return orderList.Count;}} public void AddOrder(IAgentOrder value){orderList.Add(value);}
  public void PickNewOrder(){if(order!=null && order.WantsControl() && order!=nullOrder)return;foreach(IAgentOrder candidate in orderList)if(candidate.WantsControl()){order=candidate;orderMono=order as UnityEngine.MonoBehaviour;break;}}
 }
 public class Pirate:UnityEngine.MonoBehaviour,IAgentOrder,IBrainAction {public bool aboard=true;public bool WantsControl(){return aboard;}}
 public class Combat:UnityEngine.MonoBehaviour,IAgentOrder {public bool WantsControl(){return true;}}
 public class Agent {public Brain brain=new Brain();public Pirate pirate=new Pirate();public T GetComponent<T>() where T:class{return pirate as T;}}
 public class Longship {public System.Collections.Generic.List<Agent> agents=new System.Collections.Generic.List<Agent>();}
}
namespace HarmonyLib {
 public class Traverse {private object value;public static Traverse Create(object v){return new Traverse{value=v};}public FieldValue<T> Field<T>(string n){return new FieldValue<T>{Value=(T)value.GetType().GetField(n,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(value)};}}
 public class FieldValue<T>{public T Value;}
}
namespace BadNorthMP.Game {
 public static class ShipOrdersTest {
  public static void Run(){var ship=new Voxels.TowerDefense.Longship();var agent=new Voxels.TowerDefense.Agent();ship.agents.Add(agent);var combat=new Voxels.TowerDefense.Combat();agent.brain.AddOrder(combat);agent.brain.order=combat;
  agent.brain.PickNewOrder();if(agent.brain.order!=combat)throw new System.Exception("Test must reproduce retained combat order");
  CustomShipOrders.Assign(ship);
  if(agent.brain.order!=agent.pirate || agent.brain.orderMono!=agent.pirate || agent.brain.actions[0]!=agent.pirate || agent.brain.OrderCount!=2)throw new System.Exception("Disembark order not registered and selected");
  CustomShipOrders.Assign(ship);if(agent.brain.actions.Count!=1 || agent.brain.OrderCount!=2)throw new System.Exception("Duplicate registration");
  agent.pirate.aboard=false;agent.brain.actions.Remove(agent.pirate);agent.brain.PickNewOrder();if(agent.brain.order!=combat || agent.brain.orderMono!=combat)throw new System.Exception("Failed handoff back to combat");}
 }
}
