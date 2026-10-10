$ErrorActionPreference = 'Stop'
$files = @('src/Net/UnitVisualStream.cs', 'src/Spectator/UnitVisualReceiver.cs', 'src/Spectator/VisualOnlyPrefab.cs', 'tools/test-unitvisuals-stubs.cs')
$usings = [Collections.Generic.HashSet[string]]::new()
$bodies = foreach ($file in $files) {
    $source = Get-Content -LiteralPath (Join-Path "$PSScriptRoot/.." $file) -Raw
    foreach ($match in [regex]::Matches($source, '(?m)^using [^\r\n]+;')) { [void]$usings.Add($match.Value) }
    [regex]::Replace($source, '(?m)^using [^\r\n]+;\r?\n', '')
}
Add-Type -TypeDefinition (($usings -join "`n") + "`n" + ($bodies -join "`n"))
[BadNorthMP.Net.UnitVisualTests]::Run()
Write-Output 'Actual unit visual source passed codec, packet budget, all-layer/child animator, transition, parameter refresh, hierarchy, sprite/equipment, hidden/dying, pool safety and animation-event guard tests (Unity mocked).'
