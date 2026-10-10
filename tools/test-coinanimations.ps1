$ErrorActionPreference = 'Stop'
$source = Get-Content "$PSScriptRoot/../src/Spectator/CoinVisuals.cs" -Raw
$start = $source.IndexOf('private static class AnimationCodec')
$brace = $source.IndexOf('{', $start)
$depth = 1
$end = $brace + 1
while ($depth -gt 0) {
    if ($source[$end] -eq '{') { $depth++ }
    if ($source[$end] -eq '}') { $depth-- }
    $end++
}
$codec = $source.Substring($start, $end - $start)
$stub = Get-Content "$PSScriptRoot/test-coinanimations-stubs.cs" -Raw
$test = @'
using System;
using System.IO;
using UnityEngine;
public static class CoinCodecTests {
 CODEC
 public static void Run() {
  var sender = new Animator();
  sender.SetBool(11, true); sender.SetInteger(12, 7); sender.SetFloat(13, .75f);
  using (var bytes = new MemoryStream()) {
   AnimationCodec.Write(new BinaryWriter(bytes), sender);
   bytes.Position = 0;
   var receiver = new Animator();
   AnimationCodec.Read(new BinaryReader(bytes), receiver);
   if (bytes.Position != bytes.Length || receiver.GetBool(11) != true || receiver.GetInteger(12) != 7 || receiver.GetFloat(13) != .75f)
    throw new Exception("Coin parameter round-trip failed");
   if (receiver.hashes[0] != 101 || receiver.hashes[1] != 102 || receiver.times[0] != 1.2f || receiver.weights[1] != .8f)
    throw new Exception("Coin overlays/nonloop phase were lost");
   if (receiver.fireEvents || receiver.applyRootMotion || receiver.updateCalls != 1)
    throw new Exception("Native coin/house action events remain enabled");
   bytes.Position = 0;
   var noAnimator = new Animator { layerCount = 0 };
   AnimationCodec.Read(new BinaryReader(bytes), noAnimator);
   if (bytes.Position != bytes.Length) throw new Exception("Missing layers desynchronized packet");
  }
  using (var bytes = new MemoryStream()) {
   AnimationCodec.Write(new BinaryWriter(bytes), null);
   bytes.Position = 0; AnimationCodec.Read(new BinaryReader(bytes), null);
   if (bytes.Position != bytes.Length) throw new Exception("Missing animator packet failed");
  }
 }
}
'@
$test = $test.Replace(' CODEC', $codec)
Add-Type -TypeDefinition ($test + $stub)
[CoinCodecTests]::Run()
Write-Output 'Actual coin/house codec passed layer overlays, one-shot phase, parameter types, absent layers/animators, and action-event suppression (Unity mocked).'
