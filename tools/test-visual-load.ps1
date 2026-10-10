$ErrorActionPreference = 'Stop'
$files = @('src/Net/UnitVisualStream.cs', 'src/Spectator/UnitVisualReceiver.cs', 'src/Spectator/VisualOnlyPrefab.cs', 'tools/test-unitvisuals-stubs.cs')
$usings = [Collections.Generic.HashSet[string]]::new()
$bodies = foreach ($file in $files) {
    $source = Get-Content -LiteralPath (Join-Path "$PSScriptRoot/.." $file) -Raw
    if ($file -eq 'tools/test-unitvisuals-stubs.cs') {
        $source = $source.Replace('public int events, updates;', 'public bool overwriteChildPose; public int events, updates;')
        $source = $source.Replace('public void Update(float dt) { updates++;', 'public void Update(float dt) { if(overwriteChildPose && transform.childCount>0) transform.GetChild(0).localPosition=new Vector3(0,-99,0); updates++;')
    }
    foreach ($match in [regex]::Matches($source, '(?m)^using [^\r\n]+;')) { [void]$usings.Add($match.Value) }
    [regex]::Replace($source, '(?m)^using [^\r\n]+;\r?\n', '')
}
$harness = @'
namespace BadNorthMP.Net {
 public static class VisualLoadTests {
  const int Count=100;
  static void Check(bool ok,string why) { if(!ok)throw new System.Exception(why); }
  static UnityEngine.GameObject Child(UnityEngine.GameObject root,string name) {
   var child=new UnityEngine.GameObject(name);child.transform.SetParent(root.transform);return child;
  }
  public static string Run() {
   var source=new Voxels.TowerDefense.Agent[Count];
   var copies=new Voxels.TowerDefense.Agent[Count];
   var senders=new UnitVisualStream.Sender[Count];
   var receivers=new BadNorthMP.Spectator.UnitVisualReceiver[Count];
   var animations=new UnityEngine.Animator[Count];
   var targetAnimations=new UnityEngine.Animator[Count];
   var arms=new UnityEngine.Transform[Count];
   var pools=new UnityEngine.GameObject[Count];
   var childCopies=new UnityEngine.Transform[Count];
   var renderSprites=new Voxels.TowerDefense.SpriteMagic.SpriteAnimator[Count];
   for(int id=0;id<Count;id++) {
    var go=new UnityEngine.GameObject("Agent");source[id]=go.Add<Voxels.TowerDefense.Agent>();
    animations[id]=go.Add<UnityEngine.Animator>();
    animations[id].parameters=new[]{new UnityEngine.AnimatorControllerParameter{nameHash=1,type=UnityEngine.AnimatorControllerParameterType.Float}};
    animations[id].SetFloat(1,1);
    var limb=Child(go,"Weapon");arms[id]=limb.transform;arms[id].localPosition=new UnityEngine.Vector3(0,3,0);
    limb.Add<Voxels.TowerDefense.SpriteMagic.SpriteAnimator>();
    for(int n=0;n<6;n++)Child(go,"Part"+n);
    var copy=new UnityEngine.GameObject("Agent");copies[id]=copy.Add<Voxels.TowerDefense.Agent>();
    targetAnimations[id]=copy.Add<UnityEngine.Animator>();targetAnimations[id].parameters=animations[id].parameters;
    targetAnimations[id].overwriteChildPose=true;
    var limbCopy=Child(copy,"Weapon");childCopies[id]=limbCopy.transform;
    renderSprites[id]=limbCopy.Add<Voxels.TowerDefense.SpriteMagic.SpriteAnimator>();
    for(int n=0;n<6;n++)Child(copy,"Part"+n);
    senders[id]=new UnitVisualStream.Sender();receivers[id]=new BadNorthMP.Spectator.UnitVisualReceiver();
   }
   BattleStream.packets.Clear();
   long bytes=0;int packets=0;var watch=System.Diagnostics.Stopwatch.StartNew();
   for(int frame=0;frame<180;frame++) {
    float now=frame/60f;
    if(frame%3==0) {
     for(int id=0;id<Count;id++) {
      animations[id].states[0]=new UnityEngine.AnimatorStateInfo{fullPathHash=101+id%8,normalizedTime=now,loop=true};
      animations[id].states[1]=new UnityEngine.AnimatorStateInfo{fullPathHash=202+id%8,normalizedTime=now*.5f,loop=false};
      senders[id].Capture(id,source[id],now);
     }
     UnitVisualStream.FlushFrames();
     foreach(var packet in BattleStream.packets) {
      Check(packet.Length<=1100,"packet exceeded native datagram budget");bytes+=packet.Length;packets++;
      using(var r=new System.IO.BinaryReader(new System.IO.MemoryStream(packet))) {
       var type=(MsgType)r.ReadByte();
       if(type==UnitVisualStream.NodeDefinition)receivers[r.ReadInt32()].Define(r);
       else if(type==UnitVisualStream.NodeGone)receivers[r.ReadInt32()].Remove(r.ReadUInt16());
       else BadNorthMP.Spectator.UnitVisualReceiver.ReceivePacket(type,r,id=>receivers[id]);
      }
     }
     BattleStream.packets.Clear();
    }
    if(frame<9)continue;
    for(int id=0;id<Count;id++) {
     receivers[id].Apply(copies[id],now-.12f);
     Check(targetAnimations[id].states[0].fullPathHash==101+id%8,"walking state lost");
     Check(targetAnimations[id].states[1].fullPathHash==202+id%8,"specialized attack layer lost");
     Check(System.Math.Abs(childCopies[id].localPosition.y-3)<.001f,"native animation overwrote cached scripted equipment pose");
     Check(targetAnimations[id].events==0,"visual playback fired a gameplay callback");
    }
   }
   watch.Stop();
   int evaluations=0;for(int id=0;id<Count;id++)evaluations+=targetAnimations[id].updates;
   Check(evaluations<=Count*100,"live animator evaluation still ran at render frequency instead of 30 Hz");
   for(int id=0;id<Count;id++)Check(renderSprites[id].scales<=2 && renderSprites[id].colors<=2,"stable meshes were rewritten during live animation");
   // Repeated attachment/removal must not leave history that grows through the battle.
   var definitionField=typeof(BadNorthMP.Spectator.UnitVisualReceiver).GetField("definitions",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
   var frameField=typeof(BadNorthMP.Spectator.UnitVisualReceiver).GetField("frames",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
   var activeField=typeof(UnitVisualStream.Sender).GetField("activeNodes",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
   int baseDefinitions=((System.Collections.IDictionary)definitionField.GetValue(receivers[0])).Count;
   int baseFrames=((System.Collections.IDictionary)frameField.GetValue(receivers[0])).Count;
   for(int cycle=0;cycle<100;cycle++) {
    var item=Child(source[0].gameObject,"TransientPart");var copy=Child(copies[0].gameObject,"TransientPart");
    item.Add<Voxels.TowerDefense.SelfPoolingPrefab>();item.Add<UnityEngine.Animator>();copy.Add<UnityEngine.Animator>();
    float stamp=4+cycle*.3f;
    senders[0].Capture(0,source[0],stamp);DeliverZero(receivers[0]);receivers[0].Apply(copies[0],stamp);
    item.transform.SetParent(null);
    senders[0].Capture(0,source[0],stamp+.15f);DeliverZero(receivers[0]);receivers[0].Apply(copies[0],stamp+.15f);
    UnityEngine.Object.Destroy(item);copy.transform.SetParent(null);UnityEngine.Object.Destroy(copy);
    Check(((System.Collections.IDictionary)definitionField.GetValue(receivers[0])).Count==baseDefinitions,"removed equipment definitions accumulated");
    Check(((System.Collections.IDictionary)frameField.GetValue(receivers[0])).Count==baseFrames,"removed equipment animation history accumulated");
    Check(((System.Collections.IList)activeField.GetValue(senders[0])).Count==baseDefinitions,"removed equipment stayed in sender capture loop");
   }
   return "100 units x 180 renders, two animation layers and eight hierarchy nodes: passed; "+
    "100 pooled-item cycles retained no extra active nodes or animation histories. "+
    "Live animator evaluations: "+evaluations+" (30 Hz cap). Mocked three-second stream: "+packets+" packets, "+bytes+" bytes, "+watch.ElapsedMilliseconds+" ms processing (not a Unity FPS measurement).";
  }
  static void DeliverZero(BadNorthMP.Spectator.UnitVisualReceiver receiver) {
   UnitVisualStream.FlushFrames();
   foreach(var packet in BattleStream.packets)using(var r=new System.IO.BinaryReader(new System.IO.MemoryStream(packet))) {
    var type=(MsgType)r.ReadByte();if(type==UnitVisualStream.NodeDefinition){r.ReadInt32();receiver.Define(r);}
    else if(type==UnitVisualStream.NodeGone){r.ReadInt32();receiver.Remove(r.ReadUInt16());}
    else BadNorthMP.Spectator.UnitVisualReceiver.ReceivePacket(type,r,id=>receiver);
   }
   BattleStream.packets.Clear();
  }
 }
}
'@
Add-Type -TypeDefinition (($usings -join "`n") + "`n" + ($bodies -join "`n") + "`n" + $harness)
[BadNorthMP.Net.VisualLoadTests]::Run()
