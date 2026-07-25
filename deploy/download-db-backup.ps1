param(
  [string]$HostIp = "",
  [string]$User = "root",
  [string]$Password = "",

  # Root folder on the VPS where AztecTrials is installed.
  [string]$RemoteDir = "/opt/aztectrials",
  [string]$ComposeProject = "aztectrials",

  # Where to store backups on this PC.
  # Default: <repo>/deploy/backups
  [string]$BackupDir = "",

  # Optional: prefix for the dump filename.
  [string]$BackupPrefix = "aztectrials-postgres",

  # If set, do not delete the remote /tmp dump after download.
  [switch]$KeepRemoteFile,

  # If set, only create the dump on the VPS; skip download.
  [switch]$CreateOnly,

  [string]$PuTTYDir = "C:\Program Files\PuTTY"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($HostIp)) {
  $HostIp = @(
    $env:BUGGYPYRAMID_HOST_IP,
    $env:BUGGYPYRAMID_VPS_IP,
    $env:BUGGYPYRAMID_VPS_HOST,
    $env:VPS_HOST,
    $env:HOST_IP
  ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1

  if ([string]::IsNullOrWhiteSpace($HostIp)) {
    throw "HostIp not provided; set BUGGYPYRAMID_HOST_IP (or pass -HostIp)."
  }
}

if ([string]::IsNullOrWhiteSpace($Password)) {
  $Password = @(
    $env:BUGGYPYRAMID_SSH_PASSWORD,
    $env:SSH_PASSWORD
  ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1

  if ([string]::IsNullOrWhiteSpace($Password)) {
    throw "Password not provided; set BUGGYPYRAMID_SSH_PASSWORD (or pass -Password)."
  }
}

function Require-File([string]$path, [string]$name) {
  if (-not (Test-Path $path)) {
    throw "Missing $name at: $path"
  }
}

function New-BackupFileName([string]$prefix) {
  $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
  return "$prefix-$stamp.dump"
}

$pscp = Join-Path $PuTTYDir "pscp.exe"
$plink = Join-Path $PuTTYDir "plink.exe"
Require-File $pscp "pscp.exe (PuTTY)"
Require-File $plink "plink.exe (PuTTY)"

if ([string]::IsNullOrWhiteSpace($BackupDir)) {
  $BackupDir = Join-Path $PSScriptRoot "backups"
}

if (-not (Test-Path $BackupDir)) {
  New-Item -ItemType Directory -Path $BackupDir | Out-Null
}

$backupName = New-BackupFileName $BackupPrefix
$remoteDumpPath = "/tmp/$backupName"
$localDumpPath = Join-Path $BackupDir $backupName

$remoteDeployDir = "$RemoteDir/src/deploy"

Write-Host "Creating Postgres dump on VPS -> $remoteDumpPath"
Write-Host "  Host: $User@$HostIp"
Write-Host "  Compose dir: $remoteDeployDir"

# Create dump on VPS by running pg_dump from the Postgres container.
# Notes:
# - `docker compose exec` runs pg_dump inside the db container.
# - stdout is redirected on the host to /tmp/... (so we can pscp it).
# - `-Fc` creates a custom-format dump (good for pg_restore).
$remoteCmd = @'
set -euo pipefail
DEPLOY_DIR="__DEPLOY_DIR__"
OUT="__OUT__"

cd "$DEPLOY_DIR"

if [ ! -f docker-compose.yml ]; then
  echo "ERROR: docker-compose.yml not found in $DEPLOY_DIR" >&2
  ls -la "$DEPLOY_DIR" >&2 || true
  exit 1
fi

if [ ! -f .env ]; then
  echo "ERROR: deploy/.env not found in $DEPLOY_DIR" >&2
  echo "Run the deploy script once (or create deploy/.env) so DB env vars exist." >&2
  exit 1
fi

# Sanity check the db container is up.
if ! docker compose -p "__COMPOSE_PROJECT__" -f docker-compose.yml --env-file .env ps db >/dev/null 2>&1; then
  echo "ERROR: docker compose cannot inspect the 'db' service in $DEPLOY_DIR" >&2
  exit 1
fi

# Dump.
docker compose -p "__COMPOSE_PROJECT__" -f docker-compose.yml --env-file .env exec -T db sh -lc 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > "$OUT"

# Keep perms restrictive.
chmod 600 "$OUT" || true

echo "OK: wrote $OUT" >&2
'@

$remoteCmd = $remoteCmd.
  Replace('__DEPLOY_DIR__', $remoteDeployDir).
  Replace('__COMPOSE_PROJECT__', $ComposeProject).
  Replace('__OUT__', $remoteDumpPath)

# plink -m runs via remote login shell; to ensure bash-isms (pipefail), explicitly use bash.
$remoteBashPath = "/tmp/aztectrials-db-backup.sh"
$localCmdFile = Join-Path $env:TEMP ("aztectrials-db-backup-" + [Guid]::NewGuid().ToString('N') + ".txt")

try {
  $cmdFile = @(
    "set -e",
    "cat > $remoteBashPath <<'EOF_AZTECTRIALS'",
    $remoteCmd,
    "EOF_AZTECTRIALS",
    "bash $remoteBashPath",
    "rm -f $remoteBashPath"
  ) -join "`n"

  $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
  [System.IO.File]::WriteAllText($localCmdFile, $cmdFile, $utf8NoBom)

  & $plink -batch -pw $Password -m $localCmdFile "$User@$HostIp"
  if ($LASTEXITCODE -ne 0) {
    throw "plink failed with exit code $LASTEXITCODE"
  }
} finally {
  if (Test-Path $localCmdFile) { Remove-Item $localCmdFile -Force -ErrorAction SilentlyContinue }
}

if ($CreateOnly) {
  Write-Host "Created remote dump only (skipped download): $remoteDumpPath"
  Write-Host "To download later:"
  Write-Host "  $pscp -pw <password> $User@${HostIp}:$remoteDumpPath $BackupDir"
  exit 0
}

Write-Host "Downloading dump via pscp -> $localDumpPath"
& $pscp -pw $Password "$User@${HostIp}:$remoteDumpPath" $localDumpPath
if ($LASTEXITCODE -ne 0) {
  throw "pscp failed with exit code $LASTEXITCODE"
}

Write-Host "Downloaded OK: $localDumpPath"

if (-not $KeepRemoteFile) {
  Write-Host "Cleaning up remote temp file: $remoteDumpPath"
  & $plink -batch -pw $Password "$User@$HostIp" "rm -f '$remoteDumpPath'"
  if ($LASTEXITCODE -ne 0) {
    throw "plink cleanup failed with exit code $LASTEXITCODE"
  }
}

Write-Host "Done. Restore example (on a machine with Postgres tools):"
Write-Host "  pg_restore --clean --if-exists -d <db_name> '$localDumpPath'"