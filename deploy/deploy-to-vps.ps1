param(
  [string]$HostIp = "",
  [string]$User = "root",
  [string]$Password = "",
  [string]$CredentialFile = "",
  [string]$RemoteDir = "/opt/aztectrials",
  [string]$ExistingRemoteDir = "/opt/buggypyramid",
  [string]$ExistingPublicContainer = "deploy-public-1",
  [string]$ComposeProject = "aztectrials",
  [string]$TarName = "aztectrials-src.tgz",
  [string]$PublicHost = "aztec.blobfishman.xyz",
  [string]$AdminBindIp = "192.168.178.27",
  [int]$AdminPort = 8082,
  [string]$LetsEncryptEmail = "",
  [string]$DnsExpectedIp = "",
  [string]$DnsServer = "1.1.1.1",
  [switch]$SkipDnsPreflight,
  [switch]$SkipEdgeInstall,
  [switch]$RunSetup,
  [string]$PuTTYDir = "C:\Program Files\PuTTY"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-EnvFirst([string[]]$names) {
  foreach ($name in $names) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if (-not [string]::IsNullOrWhiteSpace($value)) { return $value }
  }
  return ""
}

function Require-File([string]$path, [string]$name) {
  if (-not (Test-Path $path)) {
    throw "Missing $name at: $path"
  }
}

function Test-PrivateIPv4([string]$value) {
  $ip = $null
  if (-not [System.Net.IPAddress]::TryParse($value, [ref]$ip)) { return $false }
  $bytes = $ip.GetAddressBytes()
  if ($bytes.Length -ne 4) { return $false }
  if ($bytes[0] -eq 10) { return $true }
  if ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) { return $true }
  if ($bytes[0] -eq 192 -and $bytes[1] -eq 168) { return $true }
  return $false
}

function Load-DeployCredentials([string]$path) {
  if ([string]::IsNullOrWhiteSpace($path)) {
    $path = Join-Path $PSScriptRoot "credentials.txt"
  }
  if (-not (Test-Path $path)) { return }

  $values = @()
  $named = @{}
  foreach ($line in (Get-Content $path -ErrorAction Stop)) {
    $trim = $line.Trim()
    if ($trim -eq "" -or $trim.StartsWith("#")) { continue }
    if ($trim -match '^\s*([A-Za-z_][A-Za-z0-9_\-]*)\s*[=:]\s*(.*)$') {
      $named[$Matches[1].ToLowerInvariant()] = $Matches[2].Trim().Trim('"')
    } else {
      $values += $trim.Trim('"')
    }
  }

  if ([string]::IsNullOrWhiteSpace($script:HostIp)) {
    foreach ($key in @("hostip", "host", "vpshost", "vps", "ip")) {
      if ($named.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace($named[$key])) {
        $script:HostIp = $named[$key]
        break
      }
    }
    if ([string]::IsNullOrWhiteSpace($script:HostIp) -and $values.Count -ge 1) {
      $script:HostIp = $values[0]
    }
  }

  if ([string]::IsNullOrWhiteSpace($script:Password)) {
    foreach ($key in @("password", "sshpassword", "ssh_password")) {
      if ($named.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace($named[$key])) {
        $script:Password = $named[$key]
        break
      }
    }
    if ([string]::IsNullOrWhiteSpace($script:Password) -and $values.Count -ge 2) {
      $script:Password = $values[1]
    }
  }

  foreach ($key in @("user", "username", "sshuser", "ssh_user")) {
    if ($named.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace($named[$key])) {
      $script:User = $named[$key]
      break
    }
  }
}

Load-DeployCredentials $CredentialFile

if ([string]::IsNullOrWhiteSpace($HostIp)) {
  $HostIp = Get-EnvFirst @("AZTECTRIALS_HOST_IP", "AZTECTRIALS_VPS_IP", "AZTECTRIALS_VPS_HOST", "BUGGYPYRAMID_HOST_IP", "BUGGYPYRAMID_VPS_IP", "BUGGYPYRAMID_VPS_HOST", "VPS_HOST", "HOST_IP")
  if ([string]::IsNullOrWhiteSpace($HostIp)) {
    throw "HostIp not provided; pass -HostIp, set AZTECTRIALS_HOST_IP, or create deploy/credentials.txt."
  }
}

if ([string]::IsNullOrWhiteSpace($Password)) {
  $Password = Get-EnvFirst @("AZTECTRIALS_SSH_PASSWORD", "BUGGYPYRAMID_SSH_PASSWORD", "SSH_PASSWORD")
  if ([string]::IsNullOrWhiteSpace($Password)) {
    throw "Password not provided; pass -Password, set AZTECTRIALS_SSH_PASSWORD, or create deploy/credentials.txt."
  }
}

$pscp = Join-Path $PuTTYDir "pscp.exe"
$plink = Join-Path $PuTTYDir "plink.exe"
Require-File $pscp "pscp.exe (PuTTY)"
Require-File $plink "plink.exe (PuTTY)"

if ([string]::IsNullOrWhiteSpace($PublicHost)) {
  $PublicHost = Get-EnvFirst @("AZTECTRIALS_PUBLIC_HOST", "BUGGYPYRAMID_PUBLIC_HOST", "PUBLIC_HOST")
}
if ([string]::IsNullOrWhiteSpace($PublicHost)) {
  throw "PublicHost is required."
}

if ([string]::IsNullOrWhiteSpace($AdminBindIp)) {
  $AdminBindIp = Get-EnvFirst @("AZTECTRIALS_ADMIN_BIND_IP", "BUGGYPYRAMID_ADMIN_BIND_IP", "ADMIN_BIND_IP")
}
if ([string]::IsNullOrWhiteSpace($AdminBindIp)) {
  throw "AdminBindIp is required and must be a private host address."
}
if ($AdminPort -lt 1 -or $AdminPort -gt 65535) {
  throw "AdminPort must be between 1 and 65535."
}

if ([string]::IsNullOrWhiteSpace($LetsEncryptEmail)) {
  $LetsEncryptEmail = Get-EnvFirst @("AZTECTRIALS_LETSENCRYPT_EMAIL", "LETSENCRYPT_EMAIL", "BUGGYPYRAMID_LETSENCRYPT_EMAIL")
}

if ([string]::IsNullOrWhiteSpace($DnsExpectedIp)) {
  $DnsExpectedIp = Get-EnvFirst @("AZTECTRIALS_DNS_EXPECTED_IP", "BUGGYPYRAMID_DNS_EXPECTED_IP", "DNS_EXPECTED_IP")
  if ([string]::IsNullOrWhiteSpace($DnsExpectedIp)) {
    if (Test-PrivateIPv4 $HostIp) {
      $publicIp = (& $plink -batch -pw $Password "$User@$HostIp" "curl -4 -fsS https://api.ipify.org || curl -4 -fsS https://ifconfig.me || true" 2>$null | Select-Object -First 1).Trim()
      if (-not [string]::IsNullOrWhiteSpace($publicIp)) {
        $DnsExpectedIp = $publicIp
      }
    } else {
      $DnsExpectedIp = $HostIp
    }
  }
  if ([string]::IsNullOrWhiteSpace($DnsExpectedIp)) {
    throw "DnsExpectedIp could not be determined automatically; pass -DnsExpectedIp with the VPS public IP."
  }
}

function Resolve-ARecords([string]$name, [string]$dnsServer) {
  $ips = @()
  try {
    if ([string]::IsNullOrWhiteSpace($dnsServer)) {
      $out = & nslookup $name 2>$null
    } else {
      $out = & nslookup $name $dnsServer 2>$null
    }
    if ($LASTEXITCODE -ne 0) { return @() }

    $answerSection = $false
    foreach ($line in $out) {
      if ($line -match '^\s*Name:\s*') {
        $answerSection = $true
        continue
      }
      if ($answerSection -and $line -match '^\s*Addresses?:\s*([0-9]{1,3}(\.[0-9]{1,3}){3})\s*$') {
        $ips += $Matches[1]
      }
    }
  } catch {
  }

  if ($ips.Count -gt 0) {
    return ($ips | Select-Object -Unique)
  }

  $resolveCmd = Get-Command Resolve-DnsName -ErrorAction SilentlyContinue
  if ($null -eq $resolveCmd) { return @() }

  try {
    $answers = Resolve-DnsName -Name $name -Type A -ErrorAction Stop
    foreach ($answer in $answers) {
      if ($answer.IPAddress) { $ips += $answer.IPAddress }
    }
  } catch {
    return @()
  }

  return ($ips | Where-Object { $_ } | Select-Object -Unique)
}

if (-not $SkipDnsPreflight -and -not $SkipEdgeInstall) {
  $ips = @(Resolve-ARecords $PublicHost $DnsServer)
  if ($ips.Count -eq 0) {
    throw "DNS preflight failed: no A record found for $PublicHost. Create an A record pointing to $DnsExpectedIp, or re-run with -SkipDnsPreflight while DNS propagates."
  }
  if (-not ($ips -contains $DnsExpectedIp)) {
    throw "DNS preflight failed: $PublicHost resolves to [$($ips -join ', ')], not $DnsExpectedIp. Fix DNS or pass -DnsExpectedIp."
  }
  Write-Host "DNS preflight OK: $PublicHost resolves to $DnsExpectedIp"
} else {
  Write-Host "DNS preflight skipped."
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$tarPath = Join-Path $PSScriptRoot $TarName

Write-Host "Building AztecTrials source tarball: $tarPath"
if (Test-Path $tarPath) { Remove-Item $tarPath -Force }

Push-Location $projectRoot
try {
  tar -czf $tarPath `
    --exclude=AdminWebApp/node_modules `
    --exclude=AdminWebApp/dist `
    --exclude=Server/bin `
    --exclude=Server/.env `
    --exclude=deploy/.env `
    --exclude=deploy/credentials.txt `
    --exclude=deploy/backups `
    --exclude=deploy/$TarName `
    --exclude=Library `
    --exclude=Temp `
    Server AdminWebApp deploy
} finally {
  Pop-Location
}

$remoteTar = "/root/$TarName"
$remote = "$User@${HostIp}:$remoteTar"

Write-Host "Uploading tarball via pscp -> $remote"
& $pscp -batch -pw $Password $tarPath $remote
if ($LASTEXITCODE -ne 0) {
  throw "pscp failed with exit code $LASTEXITCODE"
}

$setupLine = if ($RunSetup) { 'bash "$LIVE/deploy/vps-setup-ubuntu.sh"' } else { ':' }

$remoteCmdTemplate = @'
set -euo pipefail

PUBLIC_HOST="__PUBLIC_HOST__"
ADMIN_BIND_IP="__ADMIN_BIND_IP__"
ADMIN_PORT="__ADMIN_PORT__"
LE_EMAIL="__LE_EMAIL__"
SKIP_EDGE_INSTALL="__SKIP_EDGE_INSTALL__"
REMOTE_DIR="__REMOTE_DIR__"
EXISTING_REMOTE_DIR="__EXISTING_REMOTE_DIR__"
COMPOSE_PROJECT="__COMPOSE_PROJECT__"
EXISTING_PUBLIC_CONTAINER="__EXISTING_PUBLIC_CONTAINER__"
TAR="__REMOTE_TAR__"

LIVE="$REMOTE_DIR/src"
STAGE="$REMOTE_DIR/src.new"
OLD="$REMOTE_DIR/src.old"
EXISTING_DEPLOY="$EXISTING_REMOTE_DIR/src/deploy"
EDGE_NETWORK="aztectrials_edge"

mkdir -p "$REMOTE_DIR"
rm -rf "$STAGE"
mkdir -p "$STAGE"

tar -xzf "$TAR" -C "$STAGE"

if [ -f "$LIVE/deploy/.env" ]; then
  cp -f "$LIVE/deploy/.env" "$STAGE/deploy/.env"
fi

rm -rf "$OLD"
if [ -d "$LIVE" ]; then
  mv "$LIVE" "$OLD"
fi
mv "$STAGE" "$LIVE"

cd "$LIVE/deploy"

if [ ! -f docker-compose.yml ]; then
  echo "ERROR: docker-compose.yml not found in $(pwd)" >&2
  exit 1
fi

if [ ! -f .env ]; then
  touch .env
  chmod 600 .env
fi

set_env_var() {
  KEY="$1"
  VAL="$2"
  if [ -z "$KEY" ]; then return 1; fi
  if [ -z "$VAL" ]; then return 0; fi
  TMP=$(mktemp)
  (grep -v "^${KEY}=" .env || true) > "$TMP"
  echo "${KEY}=${VAL}" >> "$TMP"
  mv "$TMP" .env
  chmod 600 .env
}

ensure_env_var() {
  KEY="$1"
  VAL="$2"
  if ! grep -qE "^${KEY}=" .env; then
    echo "${KEY}=${VAL}" >> .env
    chmod 600 .env
  fi
}

if ! grep -qE '^BUGGYPYRAMID_ADMIN_KEY=' .env; then
  ADMINKEY=$(head -c 256 /dev/urandom | tr -dc A-Za-z0-9 | head -c 48)
  echo "BUGGYPYRAMID_ADMIN_KEY=$ADMINKEY" >> .env
  chmod 600 .env
  echo "Created AztecTrials deploy/.env with a new BUGGYPYRAMID_ADMIN_KEY"
fi

ensure_env_var "BUGGYPYRAMID_DB_NAME" "aztectrials"
ensure_env_var "BUGGYPYRAMID_DB_USER" "aztectrials"
if ! grep -qE '^BUGGYPYRAMID_DB_PASSWORD=' .env; then
  DBPASS=$(head -c 256 /dev/urandom | tr -dc A-Za-z0-9 | head -c 48)
  echo "BUGGYPYRAMID_DB_PASSWORD=$DBPASS" >> .env
  chmod 600 .env
fi

set_env_var "BUGGYPYRAMID_PUBLIC_HOST" "$PUBLIC_HOST"
set_env_var "BUGGYPYRAMID_ADMIN_BIND_IP" "$ADMIN_BIND_IP"
set_env_var "BUGGYPYRAMID_ADMIN_PORT" "$ADMIN_PORT"
set_env_var "LETSENCRYPT_EMAIL" "$LE_EMAIL"
set_env_var "COMPOSE_PROJECT_NAME" "$COMPOSE_PROJECT"

$setupLine

docker compose -p "$COMPOSE_PROJECT" -f docker-compose.yml --env-file .env up -d --build --remove-orphans

if [ "$SKIP_EDGE_INSTALL" = "1" ]; then
  echo "Skipping edge nginx/certificate install by request." >&2
  docker compose -p "$COMPOSE_PROJECT" -f docker-compose.yml --env-file .env ps
  exit 0
fi

if [ ! -f "$EXISTING_DEPLOY/docker-compose.yml" ]; then
  echo "ERROR: existing deployment compose file not found at $EXISTING_DEPLOY/docker-compose.yml" >&2
  exit 1
fi

if ! docker inspect "$EXISTING_PUBLIC_CONTAINER" >/dev/null 2>&1; then
  echo "ERROR: existing public container '$EXISTING_PUBLIC_CONTAINER' was not found. Refusing to modify edge routing." >&2
  exit 1
fi

if ! docker inspect -f '{{range $name, $_ := .NetworkSettings.Networks}}{{println $name}}{{end}}' "$EXISTING_PUBLIC_CONTAINER" | grep -qx "$EDGE_NETWORK"; then
  docker network connect "$EDGE_NETWORK" "$EXISTING_PUBLIC_CONTAINER"
fi

cd "$EXISTING_DEPLOY"
if [ -z "$LE_EMAIL" ] && [ -f .env ]; then
  LE_EMAIL=$(grep -E '^LETSENCRYPT_EMAIL=' .env | tail -n 1 | cut -d= -f2- || true)
fi

HAS_CERT=$(docker compose -f docker-compose.yml --env-file .env run --rm --entrypoint sh certbot -lc \
  "test -f '/etc/letsencrypt/live/${PUBLIC_HOST}/fullchain.pem' -a -f '/etc/letsencrypt/live/${PUBLIC_HOST}/privkey.pem' && echo yes || true")

if [ "$HAS_CERT" != "yes" ]; then
  if [ -z "$LE_EMAIL" ]; then
    echo "ERROR: no LetsEncrypt cert exists for $PUBLIC_HOST and no LetsEncrypt email was provided." >&2
    exit 1
  fi
  echo "Issuing LetsEncrypt cert for $PUBLIC_HOST using the existing edge certbot volume" >&2
  docker compose -f docker-compose.yml --env-file .env run --rm --entrypoint certbot certbot certonly \
    --webroot -w /var/www/certbot \
    -d "$PUBLIC_HOST" --cert-name "$PUBLIC_HOST" \
    --email "$LE_EMAIL" --agree-tos --no-eff-email \
    --non-interactive --keep-until-expiring
else
  echo "LetsEncrypt cert already exists for $PUBLIC_HOST" >&2
fi

TARGET_TEMPLATE="$EXISTING_DEPLOY/nginx-public.conf"
EDGE_TEMPLATE="$LIVE/deploy/aztec-edge-nginx.conf"
BACKUP_TEMPLATE="$TARGET_TEMPLATE.aztectrials.bak"
TMP_TEMPLATE=$(mktemp)

if [ ! -f "$TARGET_TEMPLATE" ]; then
  echo "ERROR: existing edge nginx template not found at $TARGET_TEMPLATE" >&2
  exit 1
fi
if [ ! -f "$EDGE_TEMPLATE" ]; then
  echo "ERROR: Aztec edge nginx template not found at $EDGE_TEMPLATE" >&2
  exit 1
fi

cp -f "$TARGET_TEMPLATE" "$BACKUP_TEMPLATE"
awk '
  /^# BEGIN AZTECTRIALS MANAGED BLOCK$/ { skip=1; next }
  /^# END AZTECTRIALS MANAGED BLOCK$/ { skip=0; next }
  !skip { print }
' "$TARGET_TEMPLATE" > "$TMP_TEMPLATE"
printf '\n' >> "$TMP_TEMPLATE"
sed "s|AZTECTRIALS_PUBLIC_HOST_PLACEHOLDER|$PUBLIC_HOST|g" "$EDGE_TEMPLATE" >> "$TMP_TEMPLATE"
mv "$TMP_TEMPLATE" "$TARGET_TEMPLATE"

if ! docker compose -f docker-compose.yml --env-file .env restart public; then
  echo "ERROR: existing public nginx restart failed; restoring previous template." >&2
  cp -f "$BACKUP_TEMPLATE" "$TARGET_TEMPLATE"
  docker compose -f docker-compose.yml --env-file .env restart public || true
  exit 1
fi

if ! docker exec "$EXISTING_PUBLIC_CONTAINER" nginx -t; then
  echo "ERROR: existing public nginx config test failed; restoring previous template." >&2
  cp -f "$BACKUP_TEMPLATE" "$TARGET_TEMPLATE"
  docker compose -f docker-compose.yml --env-file .env restart public || true
  exit 1
fi

echo "--- AztecTrials compose status ---"
cd "$LIVE/deploy"
docker compose -p "$COMPOSE_PROJECT" -f docker-compose.yml --env-file .env ps

echo "--- Existing deployment status ---"
cd "$EXISTING_DEPLOY"
docker compose -f docker-compose.yml --env-file .env ps

echo "--- HTTP checks through existing edge ---"
PAYLOAD=$(printf '{"clientkey":"VRC_PUBLIC_CLIENT_KEY_PLACEHOLDER_0000"}' | base64 -w0)
curl -fsS --resolve "${PUBLIC_HOST}:443:127.0.0.1" "https://${PUBLIC_HOST}/data/top10/${PAYLOAD}" | head -c 300
printf '\n'

ROOT_STATUS=$(curl -ksS -o /dev/null -w '%{http_code}' --resolve "${PUBLIC_HOST}:443:127.0.0.1" "https://${PUBLIC_HOST}/" || true)
ADMIN_STATUS=$(curl -ksS -o /dev/null -w '%{http_code}' --resolve "${PUBLIC_HOST}:443:127.0.0.1" "https://${PUBLIC_HOST}/admin/" || true)
RANDOM_STATUS=$(curl -ksS -o /dev/null -w '%{http_code}' --resolve "${PUBLIC_HOST}:443:127.0.0.1" "https://${PUBLIC_HOST}/not-an-api-route" || true)

if [ "$ROOT_STATUS" != "000" ]; then
  echo "ERROR: expected Aztec public root to close with nginx 444 (curl status 000), got $ROOT_STATUS" >&2
  exit 1
fi
if [ "$ADMIN_STATUS" != "404" ]; then
  echo "ERROR: expected Aztec public /admin/ to return 404, got $ADMIN_STATUS" >&2
  exit 1
fi
if [ "$RANDOM_STATUS" != "404" ]; then
  echo "ERROR: expected Aztec public non-API route to return 404, got $RANDOM_STATUS" >&2
  exit 1
fi

echo "--- Private Admin Web App check ---"
curl -fsS "http://${ADMIN_BIND_IP}:${ADMIN_PORT}/" | grep -q '<!doctype html>'

ADMIN_PORT_BINDING=$(docker port aztectrials-admin-1 80/tcp)
if [ "$ADMIN_PORT_BINDING" != "${ADMIN_BIND_IP}:${ADMIN_PORT}" ]; then
  echo "ERROR: Aztec admin has unexpected host binding: $ADMIN_PORT_BINDING" >&2
  exit 1
fi

OLD_HOST=$(grep -E '^BUGGYPYRAMID_PUBLIC_HOST=' "$EXISTING_DEPLOY/.env" | tail -n 1 | cut -d= -f2- || true)
if [ -n "$OLD_HOST" ]; then
  curl -fsS --resolve "${OLD_HOST}:443:127.0.0.1" "https://${OLD_HOST}/_diag/ping"
fi

docker ps --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}'
'@

$remoteCmd = $remoteCmdTemplate.
  Replace('__REMOTE_DIR__', $RemoteDir).
  Replace('__EXISTING_REMOTE_DIR__', $ExistingRemoteDir).
  Replace('__REMOTE_TAR__', $remoteTar).
  Replace('__PUBLIC_HOST__', $PublicHost).
  Replace('__ADMIN_BIND_IP__', $AdminBindIp).
  Replace('__ADMIN_PORT__', $AdminPort.ToString()).
  Replace('__LE_EMAIL__', $LetsEncryptEmail).
  Replace('__SKIP_EDGE_INSTALL__', ($(if ($SkipEdgeInstall) { '1' } else { '0' }))).
  Replace('__COMPOSE_PROJECT__', $ComposeProject).
  Replace('__EXISTING_PUBLIC_CONTAINER__', $ExistingPublicContainer).
  Replace('$setupLine', $setupLine)

function New-TempFilePath([string]$prefix, [string]$suffix) {
  $name = "$prefix$([Guid]::NewGuid().ToString('N'))$suffix"
  return (Join-Path $env:TEMP $name)
}

$remoteBashPath = "/tmp/aztectrials-deploy.sh"
$remoteCmdsPath = New-TempFilePath "aztectrials-plink-" ".txt"
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
  [System.IO.File]::WriteAllText($remoteCmdsPath, $cmdFile, $utf8NoBom)

  Write-Host "Deploying AztecTrials on VPS via plink"
  & $plink -batch -pw $Password -m $remoteCmdsPath "$User@$HostIp"
  if ($LASTEXITCODE -ne 0) {
    throw "plink failed with exit code $LASTEXITCODE"
  }
} finally {
  if (Test-Path $remoteCmdsPath) { Remove-Item $remoteCmdsPath -Force -ErrorAction SilentlyContinue }
}

Write-Host "Done. AztecTrials API: https://${PublicHost}"
Write-Host "Private AztecTrials Admin: http://${AdminBindIp}:${AdminPort}/"