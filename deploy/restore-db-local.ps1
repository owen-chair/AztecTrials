param(
  # Path to a pg_dump custom-format file (*.dump) created with `pg_dump -Fc`.
  [Parameter(Mandatory = $true)]
  [string]$DumpPath,

  # Local database name to restore into.
  [string]$DbName = "buggypyramid",

  # Local Postgres connection parameters.
  [string]$Host = "localhost",
  [int]$Port = 5432,
  [string]$User = "postgres",

  # Optional password (if not provided, libpq may prompt or use pgpass).
  [string]$Password = "",

  # If set and the DB already exists, drop and recreate it.
  [switch]$DropExisting,

  # If set, do not pass --clean/--if-exists to pg_restore.
  [switch]$NoClean,

  # Optional: directory containing psql.exe/pg_restore.exe (e.g. C:\Program Files\PostgreSQL\16\bin)
  [string]$PgBinDir = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Find-PgTool([string]$name, [string]$binDir) {
  if (-not [string]::IsNullOrWhiteSpace($binDir)) {
    $candidate = Join-Path $binDir $name
    if (Test-Path $candidate) { return $candidate }
  }

  $cmd = Get-Command $name -ErrorAction SilentlyContinue
  if ($null -ne $cmd -and $cmd.Source) { return $cmd.Source }

  throw "Could not find $name. Ensure PostgreSQL is installed and its bin folder is on PATH, or pass -PgBinDir."
}

function With-PgPassword([string]$password, [scriptblock]$action) {
  $had = Test-Path Env:PGPASSWORD
  $old = $env:PGPASSWORD
  try {
    if (-not [string]::IsNullOrWhiteSpace($password)) {
      $env:PGPASSWORD = $password
    }
    & $action
  } finally {
    if ($had) {
      $env:PGPASSWORD = $old
    } else {
      Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    }
  }
}

if (-not (Test-Path $DumpPath)) {
  throw "Dump file not found: $DumpPath"
}

$DumpPath = (Resolve-Path $DumpPath).Path

$psql = Find-PgTool "psql.exe" $PgBinDir
$pgRestore = Find-PgTool "pg_restore.exe" $PgBinDir

Write-Host "Using tools:"
Write-Host "  psql:      $psql"
Write-Host "  pg_restore: $pgRestore"
Write-Host "Target DB: $DbName on $Host:$Port (user: $User)"
Write-Host "Dump: $DumpPath"

$psqlBaseArgs = @(
  "-h", $Host,
  "-p", $Port,
  "-U", $User,
  "-v", "ON_ERROR_STOP=1",
  "-X"  # do not read startup files
)

$quotedDbName = $DbName.Replace("'", "''")

With-PgPassword $Password {
  # Does the DB already exist?
  $existsSql = "SELECT 1 FROM pg_database WHERE datname='${quotedDbName}';"
  $exists = & $psql @psqlBaseArgs -d postgres -Atc $existsSql
  if ($LASTEXITCODE -ne 0) {
    throw "psql failed while checking database existence (exit=$LASTEXITCODE)."
  }

  if (-not [string]::IsNullOrWhiteSpace($exists)) {
    if (-not $DropExisting) {
      throw "Database '$DbName' already exists. Re-run with -DropExisting to drop+recreate it."
    }

    Write-Host "Dropping existing database '$DbName' (terminating connections first)..."

    $dropSql = @"
SELECT pg_terminate_backend(pid)
FROM pg_stat_activity
WHERE datname='${quotedDbName}' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS \"$DbName\";
"@

    & $psql @psqlBaseArgs -d postgres -c $dropSql
    if ($LASTEXITCODE -ne 0) {
      throw "psql failed while dropping database (exit=$LASTEXITCODE)."
    }
  }

  Write-Host "Creating database '$DbName'..."
  & $psql @psqlBaseArgs -d postgres -c "CREATE DATABASE \"$DbName\";"
  if ($LASTEXITCODE -ne 0) {
    throw "psql failed while creating database (exit=$LASTEXITCODE)."
  }

  Write-Host "Restoring dump into '$DbName'..."
  $restoreArgs = @(
    "-h", $Host,
    "-p", $Port,
    "-U", $User,
    "-d", $DbName,
    "--verbose",
    "--no-owner",
    "--no-privileges"
  )

  if (-not $NoClean) {
    $restoreArgs += "--clean"
    $restoreArgs += "--if-exists"
  }

  $restoreArgs += $DumpPath

  & $pgRestore @restoreArgs
  if ($LASTEXITCODE -ne 0) {
    throw "pg_restore failed (exit=$LASTEXITCODE)."
  }
}

Write-Host "Done. You can now connect to '$DbName' in pgAdmin/DBeaver."