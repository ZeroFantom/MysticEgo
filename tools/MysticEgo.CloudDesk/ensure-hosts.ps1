param([switch]$Elevated)
$ErrorActionPreference = 'Stop'
$mapping = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'orchestra-host.txt') -Raw).Trim() -split '\s+'
if ($mapping.Count -ne 2 -or $mapping[0] -notmatch '^\d{1,3}(\.\d{1,3}){3}$' -or $mapping[1] -notmatch '^[a-z0-9-]+(\.[a-z0-9-]+)+$') {
    throw 'Invalid Orchestra mapping in orchestra-host.txt.'
}
$ip, $name = $mapping
$hostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
function Test-HostsEntry {
    $found = $false
    foreach ($line in [IO.File]::ReadLines($hostsPath)) {
        $fields = ($line -replace '#.*$', '').Trim() -split '\s+'
        if ($fields.Count -lt 2 -or ($fields[1..($fields.Count - 1)] -notcontains $name)) { continue }
        if ($fields[0] -ne $ip) { throw "Conflicting hosts entry for $name. Verify the VPS address and correct the existing entry." }
        $found = $true
    }
    return $found
}
if (Test-HostsEntry) { exit 0 }
if ($Elevated) {
    [IO.File]::AppendAllText($hostsPath, "`r`n$ip $name # MysticEgo CloudDesk`r`n", [Text.Encoding]::ASCII)
} else {
    Write-Host "Adding $name to Windows hosts. Confirm the administrator prompt."
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Elevated'
    $process = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $arguments -Verb RunAs -Wait -PassThru
    if ($process.ExitCode -ne 0) { exit $process.ExitCode }
}
if (!(Test-HostsEntry)) { exit 1 }
