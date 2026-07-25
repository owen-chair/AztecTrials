param(
  [string]$HostIp = "",
  [string]$User = "root",
  [string]$Password = "",
  [string]$CredentialFile = "",
  [string]$PuTTYDir = "C:\Program Files\PuTTY"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($CredentialFile)) {
  $CredentialFile = Join-Path $PSScriptRoot "..\..\deploy\credentials.txt"
}
if (([string]::IsNullOrWhiteSpace($HostIp) -or [string]::IsNullOrWhiteSpace($Password)) -and (Test-Path $CredentialFile)) {
  $credentials = @(Get-Content $CredentialFile | Where-Object { $_.Trim() -ne "" -and -not $_.Trim().StartsWith("#") })
  if ([string]::IsNullOrWhiteSpace($HostIp) -and $credentials.Count -ge 1) { $HostIp = $credentials[0].Trim() }
  if ([string]::IsNullOrWhiteSpace($Password) -and $credentials.Count -ge 2) { $Password = $credentials[1].Trim() }
}
if ([string]::IsNullOrWhiteSpace($HostIp) -or [string]::IsNullOrWhiteSpace($Password)) {
  throw "Provide -HostIp and -Password, or use the existing deploy/credentials.txt format."
}

$pscp = Join-Path $PuTTYDir "pscp.exe"
$plink = Join-Path $PuTTYDir "plink.exe"
$geoIp = Join-Path $PSScriptRoot "..\..\Server\Maxmind\GeoLite2-City.mmdb"
foreach ($required in @($pscp, $plink, $geoIp)) {
  if (-not (Test-Path $required)) { throw "Required file not found: $required" }
}

$archive = Join-Path $env:TEMP ("goaccess-monitor-" + [Guid]::NewGuid().ToString("N") + ".tgz")
$remoteArchive = "/tmp/goaccess-monitor-upload.tgz"
$remoteGeoIp = "/tmp/goaccess-monitor-GeoLite2-City.mmdb"

try {
  Push-Location $PSScriptRoot
  try {
    & tar.exe -czf $archive Dockerfile docker-compose.yml goaccess.conf start.sh collector.py goaccess-collector.service logrotate.conf .env.example maintenance
    if ($LASTEXITCODE -ne 0) { throw "Failed to create GoAccess deployment archive." }
  } finally {
    Pop-Location
  }

  & $pscp -batch -pw $Password $archive "${User}@${HostIp}:$remoteArchive"
  if ($LASTEXITCODE -ne 0) { throw "Failed to upload GoAccess deployment archive." }
  & $pscp -batch -pw $Password $geoIp "${User}@${HostIp}:$remoteGeoIp"
  if ($LASTEXITCODE -ne 0) { throw "Failed to upload GeoLite2 database." }

  $remote = @'
set -eu

PROJECT_DIR=/opt/goaccess-monitor
STATE_DIR=/var/lib/goaccess-monitor

# Read-only production guards. Nothing below invokes a production lifecycle action.
test "$(docker inspect --format '{{.State.Running}}' deploy-public-1)" = true
MONITOR_CONTAINER=$(docker ps -aq --filter label=com.docker.compose.project=goaccess-monitor)
if [ -z "$MONITOR_CONTAINER" ] && ss -lntH | awk '$4 ~ /:(7890|7891)$/ { found=1 } END { exit !found }'; then
  echo 'ERROR: port 7890 or 7891 is already in use.' >&2
  exit 1
fi

install -d -m 0755 "$PROJECT_DIR" "$PROJECT_DIR/maintenance"
install -d -m 0755 "$STATE_DIR" "$STATE_DIR/logs" "$STATE_DIR/geoip"
install -d -m 0755 -o 65532 -g 65532 "$STATE_DIR/html"

tmpdir=$(mktemp -d)
trap 'rm -rf "$tmpdir" /tmp/goaccess-monitor-upload.tgz /tmp/goaccess-monitor-GeoLite2-City.mmdb' EXIT
tar -xzf /tmp/goaccess-monitor-upload.tgz -C "$tmpdir"

install -m 0644 "$tmpdir/Dockerfile" "$PROJECT_DIR/Dockerfile"
install -m 0644 "$tmpdir/docker-compose.yml" "$PROJECT_DIR/docker-compose.yml"
install -m 0644 "$tmpdir/goaccess.conf" "$PROJECT_DIR/goaccess.conf"
install -m 0755 "$tmpdir/start.sh" "$PROJECT_DIR/start.sh"
install -m 0755 "$tmpdir/collector.py" "$PROJECT_DIR/collector.py"
install -m 0644 "$tmpdir/.env.example" "$PROJECT_DIR/.env.example"
install -m 0644 "$tmpdir/maintenance/nginx-goaccess-log.conf" "$PROJECT_DIR/maintenance/nginx-goaccess-log.conf"
install -m 0644 /tmp/goaccess-monitor-GeoLite2-City.mmdb "$STATE_DIR/geoip/GeoLite2-City.mmdb"
install -m 0644 "$tmpdir/goaccess-collector.service" /etc/systemd/system/goaccess-collector.service
install -m 0644 "$tmpdir/logrotate.conf" /etc/logrotate.d/goaccess-monitor

touch "$STATE_DIR/logs/access.log"
chmod 0644 "$STATE_DIR/logs/access.log"
chown 65532:65532 "$STATE_DIR/geoip/GeoLite2-City.mmdb"

systemctl daemon-reload
systemctl enable --now goaccess-collector.service

# These are the only Docker project lifecycle commands in this deployment.
docker compose -p goaccess-monitor -f "$PROJECT_DIR/docker-compose.yml" build --pull
docker compose -p goaccess-monitor -f "$PROJECT_DIR/docker-compose.yml" up -d --wait --wait-timeout 120

echo '--- isolated GoAccess status ---'
docker compose -p goaccess-monitor -f "$PROJECT_DIR/docker-compose.yml" ps
systemctl --no-pager --full status goaccess-collector.service | sed -n '1,12p'
echo '--- LAN-only listeners ---'
ss -lntp | grep -E '192\.168\.178\.27:(7890|7891)'
echo '--- production container remained running ---'
docker inspect --format '{{.Name}} running={{.State.Running}} started={{.State.StartedAt}}' deploy-public-1
'@

  $remoteScript = Join-Path $env:TEMP ("goaccess-remote-" + [Guid]::NewGuid().ToString("N") + ".sh")
  try {
    [IO.File]::WriteAllText($remoteScript, $remote, [Text.UTF8Encoding]::new($false))
    & $plink -batch -pw $Password -m $remoteScript "${User}@${HostIp}"
    if ($LASTEXITCODE -ne 0) { throw "Remote GoAccess deployment failed." }
  } finally {
    Remove-Item $remoteScript -Force -ErrorAction SilentlyContinue
  }
} finally {
  Remove-Item $archive -Force -ErrorAction SilentlyContinue
}

Write-Host "GoAccess dashboard: http://192.168.178.27:7890/"
