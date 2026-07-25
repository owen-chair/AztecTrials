param(
  [string]$HostIp = "",
  [string]$User = "root",
  [string]$Password = "",
  [string]$RemoteDir = "/opt/aztectrials",
  [string]$TarName = "aztectrials-src.tgz",
  [string]$PuTTYDir = "C:\Program Files\PuTTY"
)

$ErrorActionPreference = "Stop"

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

$projectRoot = Split-Path -Parent $PSScriptRoot
$tarPath = Join-Path $PSScriptRoot $TarName

Write-Host "Building source tarball: $tarPath"
if (Test-Path $tarPath) { Remove-Item $tarPath -Force }

Push-Location $projectRoot
try {
  # Only ship what the VPS needs to build: Go server + AdminWebApp source + deploy files.
  # Exclude node_modules/dist to keep upload small.
  tar -czf $tarPath `
    --exclude=AdminWebApp/node_modules `
    --exclude=AdminWebApp/dist `
    --exclude=deploy/.env `
    --exclude=deploy/credentials.txt `
    --exclude=deploy/backups `
    --exclude=deploy/$TarName `
    Server AdminWebApp deploy
} finally {
  Pop-Location
}

Write-Host "Uploading tarball via pscp..."
$remote = "$User@${HostIp}:/root/$TarName"
$pscp = Join-Path $PuTTYDir "pscp.exe"
Require-File $pscp "pscp.exe (PuTTY)"
& $pscp -pw $Password $tarPath $remote

Write-Host "Uploaded to $remote"
Write-Host "Next on the VPS (over SSH):"
Write-Host "  mkdir -p $RemoteDir/src"
Write-Host "  tar -xzf /root/$TarName -C $RemoteDir/src"
Write-Host "  bash $RemoteDir/src/deploy/vps-setup-ubuntu.sh"
Write-Host "  # Prefer deploy-to-vps.ps1 for full side-by-side setup and edge routing."
Write-Host "  cd $RemoteDir/src/deploy && docker compose -p aztectrials -f docker-compose.yml --env-file .env up -d --build"
