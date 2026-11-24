# MysticEgo Agent: замена бинарников только после проверки подписи релиза в .NET.
# Работа в отдельном процессе; не запускайте скрипт напрямую на неизвестных ZIP.
param(
  [Parameter(Mandatory=$true)][string]$ServiceName,
  [Parameter(Mandatory=$true)][string]$SourceZip,
  [Parameter(Mandatory=$true)][string]$Target,
  [Parameter(Mandatory=$true)][int]$ParentPid
)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:ProgramData 'MysticEgo'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$stage = Join-Path $root ('update-' + [guid]::NewGuid().ToString('N'))
$backup = Join-Path $root ('backup-' + [guid]::NewGuid().ToString('N'))
$log = Join-Path $root 'agent-updater.log'
New-Item -ItemType Directory -Path $stage,$backup -Force | Out-Null
$changed = New-Object System.Collections.ArrayList
function Write-Log([string]$value) { Add-Content -LiteralPath $log -Value "$(Get-Date -Format o) $value" }
function Is-Configuration([string]$relative) {
  $r = $relative.Replace('\','/').ToLowerInvariant()
  $name = [System.IO.Path]::GetFileName($r)
  return ($r.StartsWith('data/') -or $r.Contains('/data/') -or
          ($name.StartsWith('appsettings') -and $name.EndsWith('.json')) -or
          $name -eq 'credentials.json' -or $name.EndsWith('.pem') -or
          $name.EndsWith('.db') -or $name.EndsWith('.sqlite'))
}

try {
  if(!(Test-Path -LiteralPath $SourceZip -PathType Leaf)) { throw 'Update ZIP not found' }
  if(!(Test-Path -LiteralPath $Target -PathType Container)) { throw 'Agent installation not found' }
  [void](Get-Service -Name $ServiceName -ErrorAction Stop)
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $zip = [System.IO.Compression.ZipFile]::OpenRead($SourceZip)
  try {
    [long]$size = 0
    if($zip.Entries.Count -gt 2000) { throw 'Too many files in update' }
    foreach ($entry in $zip.Entries) {
      $size += $entry.Length
      $entryName = $entry.FullName.Replace('\','/')
      if ($size -gt 500000000 -or $entryName.StartsWith('/') -or $entryName.Contains(':') -or ($entryName.Split('/') -contains '..')) {
        throw 'Unsafe or oversized update archive'
      }
    }
  } finally { $zip.Dispose() }
  Expand-Archive -LiteralPath $SourceZip -DestinationPath $stage -Force
  if(!(Test-Path -LiteralPath (Join-Path $stage 'Api.Agent.dll') -PathType Leaf)) { throw 'Expected Api.Agent.dll at archive root' }
  Write-Log "Stopping agent service $ServiceName"
  Stop-Service -Name $ServiceName -Force -ErrorAction Stop
  try { Wait-Process -Id $ParentPid -Timeout 40 -ErrorAction SilentlyContinue } catch {}
  foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
    $relative = $file.FullName.Substring($stage.Length).TrimStart('\')
    if (Is-Configuration $relative) { continue }
    $destination = Join-Path $Target $relative
    $saved = Join-Path $backup $relative
    $exists = Test-Path -LiteralPath $destination -PathType Leaf
    if ($exists) {
      New-Item -ItemType Directory -Path (Split-Path -Parent $saved) -Force | Out-Null
      Copy-Item -LiteralPath $destination -Destination $saved -Force -ErrorAction Stop
    }
    [void]$changed.Add([pscustomobject]@{Destination=$destination;Backup=$saved;Existed=$exists})
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force -ErrorAction Stop
  }
  Start-Service -Name $ServiceName -ErrorAction Stop
  Start-Sleep -Seconds 5
  if((Get-Service -Name $ServiceName).Status -ne 'Running') { throw 'Agent did not reach Running state' }
  Write-Log "Updated $ServiceName; backup: $backup"
} catch {
  Write-Log "Update failed: $_. Rolling back..."
  try { Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue } catch {}
  for($i=$changed.Count-1;$i -ge 0;$i--) {
    $file = $changed[$i]
    try {
      if($file.Existed) { Copy-Item -LiteralPath $file.Backup -Destination $file.Destination -Force }
      else { Remove-Item -LiteralPath $file.Destination -Force -ErrorAction SilentlyContinue }
    } catch { Write-Log "File rollback failed: $($file.Destination): $_" }
  }
  try { Start-Service -Name $ServiceName -ErrorAction Stop } catch { Write-Log "Service recovery failed: $_" }
  Write-Log "Rollback attempted; files retained in $backup"
  exit 1
} finally {
  Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}
