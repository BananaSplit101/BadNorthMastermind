$ErrorActionPreference = 'Stop'
$source = (Get-Content "$PSScriptRoot/../src/Game/BeachChoices.cs" -Raw) + (Get-Content "$PSScriptRoot/test-landingtiles-stubs.cs" -Raw)
Add-Type -TypeDefinition $source
[BadNorthMP.Game.LandingTilesTest]::Run()
Write-Output 'Actual landing-tile code passed probe hierarchy, tile containment, height, placement rejection, and marker-to-tile checks (physics mocked).'
