$ErrorActionPreference = 'Stop'
$stage = __STAGE__
$target = __TARGET__
$expected = __HASH__
$gamePid = __PID__
$rejoin = __REJOIN__
$lobby = __LOBBY__
try {
    $process = Get-Process -Id $gamePid -ErrorAction SilentlyContinue
    if ($process -and -not $process.WaitForExit(120000)) { throw 'Game did not close; update cancelled' }
    if ((Get-FileHash -LiteralPath $stage -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Checksum changed' }
    $identity = [Reflection.AssemblyName]::GetAssemblyName($stage)
    if ($identity.Name -ne 'BadNorthMP') { throw 'Unexpected assembly' }
    $backup = $target + '.previous'
    Copy-Item -LiteralPath $target -Destination $backup -Force
    try { Copy-Item -LiteralPath $stage -Destination $target -Force }
    catch { Copy-Item -LiteralPath $backup -Destination $target -Force; throw }
    Remove-Item -LiteralPath $stage -Force -ErrorAction SilentlyContinue
    Set-Content -LiteralPath $rejoin -Value $lobby -NoNewline
    Start-Process 'steam://rungameid/688420' -WindowStyle Hidden
} catch {
    $_ | Out-String | Set-Content -LiteralPath ($stage + '.error.txt')
}
