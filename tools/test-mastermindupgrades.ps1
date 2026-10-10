$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'
$sources = @(
    'src/Game/Economy.cs', 'src/Game/EconomyConfig.cs', 'src/Game/WaveSchedule.cs', 'src/Game/MastermindSession.cs',
    'src/Game/CommanderRoster.cs', 'src/Game/VikingCommanders.cs',
    'src/Game/MasterState.cs', 'src/Game/MastermindUpgrades.cs', 'src/Game/MastermindUpgradeSave.cs',
    'src/Net/MastermindNet.cs', 'tools/test-mastermindupgrades-stubs.cs'
) | ForEach-Object { Join-Path $root $_ }
Add-Type -Path $sources
[BadNorthMP.Game.MastermindUpgradeTests]::Run()
