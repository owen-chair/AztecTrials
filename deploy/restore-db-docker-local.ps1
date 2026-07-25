param(
  # Path to a pg_dump custom-format file (*.dump) created with `pg_dump -Fc`.
  [Parameter(Mandatory = $true)]
  [string]$DumpPath,

  # Docker image to use.
  [string]$PostgresImage = "postgres:16-alpine",

  # Container / volume naming.
  [string]$ContainerName = "buggypyramid-postgres-local",
  [string]$VolumeName = "buggypyramid-postgres-local-data",

  # Database settings inside the container.
  [string]$DbName = "buggypyramid",
  [string]$DbUser = "postgres",

  # Password for the postgres user in the container.
  # If blank, a random password is generated and printed.
  [string]$DbPassword = "",

  # Host port to expose for pgAdmin (container is always 5432 internally).
  [int]$HostPort = 5433,

  # If the container already exists, remove and recreate it.
  [switch]$Recreate,

  # If set, remove the Docker volume before starting (DESTROYS any previous local DB data).
  [switch]$ResetVolume
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Docker([string[]]$DockerArgs) {
  $out = & docker @DockerArgs 2>&1
  return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($out -join "`n") }
}

function Throw-DockerNotUsable([string]$details) {
  $extra = ""
  if ($details -match '(?i)virtualization support not detected') {
    $extra = @(
      "",
      "Fix (Windows):",
      "- Enable virtualization in BIOS/UEFI (Intel VT-x / AMD-V / SVM).",
      "- In 'Turn Windows features on or off': enable 'Windows Subsystem for Linux' and 'Virtual Machine Platform'.",
      "- Reboot.",
      "- Open admin PowerShell: wsl --install ; then reboot again.",
      "- Start Docker Desktop and ensure it uses WSL 2 backend.",
      "",
      "If you cannot enable virtualization on this machine (locked-down PC), you won’t be able to run Docker Desktop here; use a local PostgreSQL install + .\\restore-db-local.ps1 instead."
    ) -join "`n"
  }

  throw @(
    "Docker is installed but not usable right now.",
    "Make sure Docker Desktop is running, then re-run this script.",
    "",
    "Docker output:",
    $details,
    $extra
  ) -join "`n"
}

function Ensure-Docker {
  $cmd = Get-Command docker -ErrorAction SilentlyContinue
  if ($null -ne $cmd -and $cmd.Source) {
    return
  }

  # Common Docker Desktop locations on Windows.
  $candidates = @(
    "C:\\Program Files\\Docker\\Docker\\resources\\bin\\docker.exe",
    "C:\\Program Files\\Docker\\Docker\\resources\\docker.exe",
    "C:\\ProgramData\\DockerDesktop\\version-bin\\docker.exe"
  )

  foreach ($p in $candidates) {
    if (Test-Path $p) {
      Set-Alias -Name docker -Value $p -Scope Script
      return
    }
  }

  throw @(
    "Missing required command 'docker'.",
    "Install Docker Desktop for Windows and make sure it's running, then re-run this script.",
    "After install, open a new PowerShell window so PATH/aliases refresh.",
    "(If Docker is installed but not on PATH, you can also add: C:\\Program Files\\Docker\\Docker\\resources\\bin)"
  ) -join " `n"
}

function New-RandomPassword([int]$length = 24) {
  $bytes = New-Object byte[] 64
  [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
  $chars = ([Convert]::ToBase64String($bytes) -replace '[^A-Za-z0-9]', '')
  if ($chars.Length -lt $length) { $length = $chars.Length }
  return $chars.Substring(0, $length)
}

function Docker-ContainerExists([string]$name) {
  $id = & docker ps -a --filter "name=^/${name}$" --format "{{.ID}}"
  return -not [string]::IsNullOrWhiteSpace($id)
}

function Docker-VolumeExists([string]$name) {
  $n = & docker volume ls --format "{{.Name}}" | Where-Object { $_ -eq $name }
  return $null -ne $n
}

function Wait-ForPostgres([string]$container, [string]$db, [string]$user, [int]$timeoutSeconds = 60) {
  $start = Get-Date
  while ($true) {
    & docker exec $container pg_isready -U $user -d $db | Out-Null
    if ($LASTEXITCODE -eq 0) { return }

    if (((Get-Date) - $start).TotalSeconds -ge $timeoutSeconds) {
      throw "Timed out waiting for Postgres to become ready in container '$container'."
    }

    Start-Sleep -Seconds 1
  }
}

function Wait-ForPostgresQuery(
  [string]$container,
  [string]$db,
  [string]$user,
  [string]$password,
  [int]$timeoutSeconds = 90
) {
  $start = Get-Date
  while ($true) {
    # If the container died, show logs and fail fast.
    $running = (& docker inspect -f "{{.State.Running}}" $container 2>$null | Out-String).Trim()
    if ($running -eq "false") {
      $logs = (& docker logs --tail 200 $container 2>&1 | Out-String).Trim()
      throw "Postgres container '$container' is not running. Last logs:`n$logs"
    }

    # Run a real query; pg_isready can be optimistic during startup.
    $cmd = "PGPASSWORD='$password' psql -U '$user' -d '$db' -Atc 'SELECT 1'"
    $out = (& docker exec $container sh -lc $cmd 2>&1 | Out-String)
    $text = $out.Trim()

    # During first boot the official image may briefly start/stop the server.
    if ($text -match '(?i)database system is starting up|database system is shutting down') {
      Start-Sleep -Seconds 1
      continue
    }

    if ($LASTEXITCODE -eq 0 -and $text -eq "1") {
      return
    }

    if (((Get-Date) - $start).TotalSeconds -ge $timeoutSeconds) {
      throw "Timed out waiting for Postgres to accept queries in container '$container'. Last output: $text"
    }

    Start-Sleep -Seconds 1
  }
}

Ensure-Docker

# Fail fast with a clearer message if Docker Desktop isn't running or WSL2/virtualization isn't ready.
$ver = Invoke-Docker @("version")
if ($ver.ExitCode -ne 0) {
  Throw-DockerNotUsable $ver.Output
}

if (-not (Test-Path $DumpPath)) {
  throw "Dump file not found: $DumpPath"
}
$DumpPath = (Resolve-Path $DumpPath).Path

if ([string]::IsNullOrWhiteSpace($DbPassword)) {
  $DbPassword = New-RandomPassword 28
}

# Handle existing container.
if (Docker-ContainerExists $ContainerName) {
  if (-not $Recreate) {
    throw "Container '$ContainerName' already exists. Re-run with -Recreate (and optionally -ResetVolume)."
  }

  Write-Host "Removing existing container '$ContainerName'..."
  & docker rm -f $ContainerName | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "Failed to remove existing container '$ContainerName'." }
}

# Optionally reset data volume.
if ($ResetVolume -and (Docker-VolumeExists $VolumeName)) {
  Write-Host "Removing existing volume '$VolumeName' (data loss)..."
  & docker volume rm $VolumeName | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "Failed to remove volume '$VolumeName'." }
}

# Ensure volume exists.
if (-not (Docker-VolumeExists $VolumeName)) {
  Write-Host "Creating volume '$VolumeName'..."
  & docker volume create $VolumeName | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "Failed to create volume '$VolumeName'." }
}

Write-Host "Starting Postgres container '$ContainerName' (image: $PostgresImage) on 127.0.0.1:$HostPort ..."

& docker run -d --name $ContainerName `
  -e "POSTGRES_DB=$DbName" `
  -e "POSTGRES_USER=$DbUser" `
  -e "POSTGRES_PASSWORD=$DbPassword" `
  -p "${HostPort}:5432" `
  -v "${VolumeName}:/var/lib/postgresql/data" `
  $PostgresImage | Out-Null

if ($LASTEXITCODE -ne 0) {
  throw "docker run failed (exit=$LASTEXITCODE). Common causes: port $HostPort already in use, or Docker Desktop not running."
}

Write-Host "Waiting for Postgres to be ready..."
Wait-ForPostgres -container $ContainerName -db $DbName -user $DbUser -timeoutSeconds 90
Wait-ForPostgresQuery -container $ContainerName -db $DbName -user $DbUser -password $DbPassword -timeoutSeconds 120

# Copy dump into container.
$containerDump = "/tmp/buggypyramid-backup.dump"
Write-Host "Copying dump into container..."
& docker cp $DumpPath "${ContainerName}:$containerDump" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "docker cp failed (exit=$LASTEXITCODE)." }

Write-Host "Restoring dump (this can take a minute)..."

# Restore using pg_restore inside the container.
# --clean/--if-exists makes re-runs more predictable if the DB isn't empty.
$restoreCmd = "PGPASSWORD='$DbPassword' pg_restore --clean --if-exists --no-owner --no-privileges -U '$DbUser' -d '$DbName' '$containerDump'"
$maxRestoreAttempts = 8
for ($attempt = 1; $attempt -le $maxRestoreAttempts; $attempt++) {
  $restoreOut = & docker exec $ContainerName sh -lc $restoreCmd 2>&1
  if ($LASTEXITCODE -eq 0) {
    break
  }

  $restoreText = ($restoreOut -join "`n")
  if ($restoreText -match '(?i)database system is starting up' -and $attempt -lt $maxRestoreAttempts) {
    Write-Host "pg_restore hit 'database system is starting up' (attempt $attempt/$maxRestoreAttempts). Waiting 2s and retrying..."
    Start-Sleep -Seconds 2
    continue
  }

  throw "pg_restore failed (exit=$LASTEXITCODE). Output: $restoreText"
}

Write-Host ""
Write-Host "Local Docker Postgres is ready for pgAdmin4:"
Write-Host "  Host/IP: 127.0.0.1"
Write-Host "  Port:    $HostPort"
Write-Host "  DB:      $DbName"
Write-Host "  User:    $DbUser"
Write-Host "  Pass:    $DbPassword"
Write-Host ""
Write-Host "Tip: If you re-run, use -Recreate. If you want a totally fresh restore, add -ResetVolume."