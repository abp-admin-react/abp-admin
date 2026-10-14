# Must-run before handing off backend changes: closure gate + real docker build of both backend images.
# Usage: powershell -File scripts/docker-build-check.ps1   (requires Docker)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'check-docker-restore.ps1')
if ($LASTEXITCODE) { exit $LASTEXITCODE }
Push-Location (Join-Path $PSScriptRoot '..\backend')
try {
  docker build -f framework/AbpAdmin.HttpApi.Host/Dockerfile -t abp-admin-host .
  if ($LASTEXITCODE) { throw 'docker build host failed' }
  docker build -f framework/AbpAdmin.DbMigrator/Dockerfile -t abp-admin-migrator .
  if ($LASTEXITCODE) { throw 'docker build migrator failed' }
} finally { Pop-Location }
Write-Host 'OK: both backend images built.'
