$ErrorActionPreference = 'Stop'
$files = @('src/Net/UnitVisualStream.cs', 'src/Spectator/UnitVisualReceiver.cs', 'src/Spectator/VisualOnlyPrefab.cs', 'tools/test-unitvisuals-stubs.cs')
$usings = [Collections.Generic.HashSet[string]]::new()
$bodies = foreach ($file in $files) {
    $source = Get-Content -LiteralPath (Join-Path "$PSScriptRoot/.." $file) -Raw
    if ($file -eq 'tools/test-unitvisuals-stubs.cs') {
        # Instrument only the mocked Unity API; production serializer/receiver remain unchanged.
        $source = $source.Replace('public readonly List<Component> components', 'public static int componentQueries; public readonly List<Component> components')
        $source = $source.Replace('where T : Component { foreach (var c in components)', 'where T : Component { componentQueries++; foreach (var c in components)')
        $source = $source.Replace('where T : Component { var result = new List<T>(); foreach (var c in components)', 'where T : Component { componentQueries++; var result = new List<T>(); foreach (var c in components)')
        $source = $source.Replace('public static readonly List<Object> all', 'public static int spriteScans; public static readonly List<Object> all')
        $source = $source.Replace('where T : Object { var result=new List<T>();', 'where T : Object { if(typeof(T)==typeof(Sprite))spriteScans++; var result=new List<T>();')
        # Reproduce Unity OnEnable resetting a cached pose/controller when healed units return.
        $source = $source.Replace('public void SetActive(bool active) { activeSelf = active; }', 'public void SetActive(bool active) { if(active && !activeSelf) { if(transform.childCount>0) transform.GetChild(0).localPosition=new Vector3(0,-98,0); foreach(var c in components) if(c is Animator anim) for(int l=0;l<anim.states.Length;l++) anim.states[l]=new AnimatorStateInfo{fullPathHash=999}; } activeSelf = active; }')
        $anchor = 'Check(truncated,"truncated record silently accepted");'
        if (!$source.Contains($anchor)) { throw 'Performance test insertion point changed' }
        $extra = @'
            receiver.Apply(puppet, 3f);
            int updatesBefore = target.updates + targetChild.updates;
            int meshesBefore = copySprite.scales + copySprite.colors;
            int queriesBefore = UnityEngine.GameObject.componentQueries;
            int scansBefore = UnityEngine.Resources.spriteScans;
            for (int repeat = 0; repeat < 240; repeat++) receiver.Apply(puppet, 3f);
            Check(target.updates + targetChild.updates == updatesBefore, "unchanged samples reevaluated animators");
            Check(copySprite.scales + copySprite.colors == meshesBefore, "unchanged samples rewrote mesh buffers");
            Check(UnityEngine.GameObject.componentQueries == queriesBefore, "unchanged samples queried components");
            Check(UnityEngine.Resources.spriteScans == scansBefore, "unchanged samples scanned sprite resources");
            sender.Capture(17, agent, 3f); Deliver(receiver); receiver.Apply(puppet, 3f);
            queriesBefore = UnityEngine.GameObject.componentQueries;
            for (int repeat = 0; repeat < 100; repeat++) sender.Capture(17, agent, 3.01f);
            UnitVisualStream.FlushFrames();
            foreach (var packet in BattleStream.packets)
                Check(packet[0] != (byte)UnitVisualStream.AgentFrame && packet[0] != (byte)UnitVisualStream.NodeFrame,
                    "unchanged visibility or child poses generated extra datagrams");
            Check(UnityEngine.GameObject.componentQueries - queriesBefore <= 100,
                "sender repeated per-node component discovery");
            BattleStream.packets.Clear();
            animation.states[1] = new UnityEngine.AnimatorStateInfo { fullPathHash=909, normalizedTime=.8f, loop=false };
            sender.Capture(17, agent, 3.02f); Deliver(receiver); receiver.Apply(puppet, 3.02f);
            Check(target.states[1].fullPathHash==909, "attack state change waited for the reduced phase sampling interval");
            animation.states[1] = new UnityEngine.AnimatorStateInfo { fullPathHash=909, normalizedTime=.1f, loop=false };
            sender.Capture(17, agent, 3.03f); Deliver(receiver); receiver.Apply(puppet, 3.03f);
            Check(Near(target.states[1].normalizedTime,.1f), "restarting the same attack was lost between scheduled samples");
            source.SetActive(false); sender.Capture(17,agent,3.04f); Deliver(receiver); receiver.Apply(puppet,3.04f);
            source.SetActive(true); weapon.transform.localPosition=new UnityEngine.Vector3(0,4,0);
            animation.states[0]=new UnityEngine.AnimatorStateInfo{fullPathHash=707,normalizedTime=.2f,loop=true};
            animation.states[1]=new UnityEngine.AnimatorStateInfo{fullPathHash=909,normalizedTime=.1f,loop=false};
            sender.Capture(17,agent,3.05f); Deliver(receiver); receiver.Apply(puppet,3.05f);
            Check(copy.activeSelf && target.states[0].fullPathHash==707 && target.states[1].fullPathHash==909 && Near(copyWeapon.transform.localPosition.y,4),
                "returning healed unit kept an OnEnable-reset pose or attack layer");
'@
        $source = $source.Replace($anchor, $anchor + "`n" + $extra)
    }
    foreach ($match in [regex]::Matches($source, '(?m)^using [^\r\n]+;')) { [void]$usings.Add($match.Value) }
    [regex]::Replace($source, '(?m)^using [^\r\n]+;\r?\n', '')
}
Add-Type -TypeDefinition (($usings -join "`n") + "`n" + ($bodies -join "`n"))
[BadNorthMP.Net.UnitVisualTests]::Run()
Write-Output 'Visual regressions passed. 240 unchanged renders caused zero extra animator evaluations, mesh writes, component queries, or sprite scans; 100 unchanged captures emitted no child-pose or visibility datagrams (Unity mocked).'
