# Gate: every Dockerfile restore layer must COPY the full ProjectReference closure of its entry csproj.
# Missing csproj => dotnet restore skips it => publish --no-restore fails (NETSDK1004).
# Usage (repo root or anywhere): pwsh scripts/check-docker-restore.ps1
$ErrorActionPreference = 'Stop'
$backend = (Resolve-Path (Join-Path $PSScriptRoot '..\backend')).Path

$targets = @(
  @{ Dockerfile = 'framework/AbpAdmin.HttpApi.Host/Dockerfile'; Entry = 'framework/AbpAdmin.HttpApi.Host/AbpAdmin.HttpApi.Host.csproj' },
  @{ Dockerfile = 'framework/AbpAdmin.DbMigrator/Dockerfile';   Entry = 'framework/AbpAdmin.DbMigrator/AbpAdmin.DbMigrator.csproj' }
)

function Get-Rel([string]$full) { $full.Substring($backend.Length).TrimStart('\', '/').Replace('\', '/') }

function Get-Closure([string]$entryRel) {
  $seen = New-Object 'System.Collections.Generic.HashSet[string]'
  $stack = New-Object System.Collections.Stack
  $stack.Push((Join-Path $backend $entryRel))
  while ($stack.Count -gt 0) {
    $p = [IO.Path]::GetFullPath([string]$stack.Pop())
    if (-not $seen.Add((Get-Rel $p))) { continue }
    if (-not (Test-Path $p)) { throw "referenced csproj not found: $p" }
    [xml]$x = Get-Content -Raw -Encoding UTF8 $p
    foreach ($r in $x.SelectNodes('//ProjectReference')) {
      $inc = $r.GetAttribute('Include').Replace('\', [IO.Path]::DirectorySeparatorChar)
      $stack.Push((Join-Path (Split-Path $p) $inc))
    }
  }
  return $seen
}

$failed = $false
foreach ($t in $targets) {
  $closure = Get-Closure $t.Entry
  $copied = New-Object 'System.Collections.Generic.HashSet[string]'
  Get-Content -Encoding UTF8 (Join-Path $backend $t.Dockerfile) | ForEach-Object {
    if ($_ -match '^\s*COPY\s+(\S+\.csproj)\s') { [void]$copied.Add($Matches[1]) }
  }
  $missing = $closure | Where-Object { -not $copied.Contains($_) }
  $extra = $copied | Where-Object { -not $closure.Contains($_) }
  if ($missing) { $failed = $true; $missing | ForEach-Object { Write-Host "[FAIL] $($t.Dockerfile) restore COPY missing: $_" } }
  if ($extra) { $extra | ForEach-Object { Write-Host "[warn] $($t.Dockerfile) COPY not in closure: $_" } }
  Write-Host "$($t.Dockerfile): closure=$($closure.Count) copied=$($copied.Count)"
}
if ($failed) { exit 1 }
Write-Host 'OK: restore COPY lists cover the ProjectReference closure.'
