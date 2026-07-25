param(
  [string]$HostIp = "",
  [string]$User = "root",
  [string]$Password = "",
  [string]$CredentialFile = "",
  [string]$ExistingRemoteDir = "/opt/buggypyramid",
  [string]$PublicHost = "aztec.blobfishman.xyz",
  [string]$LetsEncryptEmail = "",
  [switch]$Force,
  [string]$PuTTYDir = "C:\Program Files\PuTTY"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Load-DeployCredentials([string]$path) {
  if ([string]::IsNullOrWhiteSpace($path)) {
    $path = Join-Path $PSScriptRoot "credentials.txt"
  }
  if (-not (Test-Path $path)) { return }

  $values = @()
  foreach ($line in (Get-Content $path -ErrorAction Stop)) {
    $trim = $line.Trim()
    if ($trim -eq "" -or $trim.StartsWith("#")) { continue }
    if ($trim -match '^\s*([A-Za-z_][A-Za-z0-9_\-]*)\s*[=:]\s*(.*)$') {
      $key = $Matches[1].ToLowerInvariant()
      $value = $Matches[2].Trim().Trim('"')
      if (($key -eq "host" -or $key -eq "hostip" -or $key -eq "ip") -and [string]::IsNullOrWhiteSpace($script:HostIp)) { $script:HostIp = $value }
      if (($key -eq "password" -or $key -eq "ssh_password") -and [string]::IsNullOrWhiteSpace($script:Password)) { $script:Password = $value }
      if (($key -eq "user" -or $key -eq "username") -and -not [string]::IsNullOrWhiteSpace($value)) { $script:User = $value }
    } else {
      $values += $trim.Trim('"')
    }
  }

  if ([string]::IsNullOrWhiteSpace($script:HostIp) -and $values.Count -ge 1) { $script:HostIp = $values[0] }
  if ([string]::IsNullOrWhiteSpace($script:Password) -and $values.Count -ge 2) { $script:Password = $values[1] }
}

Load-DeployCredentials $CredentialFile

if ([string]::IsNullOrWhiteSpace($HostIp)) {
  $HostIp = @($env:AZTECTRIALS_HOST_IP, $env:BUGGYPYRAMID_HOST_IP, $env:VPS_HOST, $env:HOST_IP) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1
  if ([string]::IsNullOrWhiteSpace($HostIp)) { throw "HostIp not provided." }
}

if ([string]::IsNullOrWhiteSpace($Password)) {
  $Password = @($env:AZTECTRIALS_SSH_PASSWORD, $env:BUGGYPYRAMID_SSH_PASSWORD, $env:SSH_PASSWORD) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1
  if ([string]::IsNullOrWhiteSpace($Password)) { throw "Password not provided." }
}

if ([string]::IsNullOrWhiteSpace($LetsEncryptEmail)) {
  $LetsEncryptEmail = @($env:AZTECTRIALS_LETSENCRYPT_EMAIL, $env:LETSENCRYPT_EMAIL, $env:BUGGYPYRAMID_LETSENCRYPT_EMAIL) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1
}

function Require-File([string]$path, [string]$name) {
  if (-not (Test-Path $path)) { throw "Missing $name at: $path" }
}

$plink = Join-Path $PuTTYDir "plink.exe"
Require-File $plink "plink.exe (PuTTY)"

$remoteBashPath = "/tmp/aztectrials-renew-cert.sh"
$cmd = @'
set -euo pipefail

EXISTING_DEPLOY="__EXISTING_REMOTE_DIR__/src/deploy"
PUBLIC_HOST="__PUBLIC_HOST__"
LE_EMAIL="__LE_EMAIL__"

cd "$EXISTING_DEPLOY"

if [ ! -f .env ]; then
  echo "Missing existing deployment .env at $EXISTING_DEPLOY/.env" >&2
  exit 1
fi

if [ -z "$LE_EMAIL" ]; then
  LE_EMAIL=$(grep -E '^LETSENCRYPT_EMAIL=' .env | tail -n 1 | cut -d= -f2- || true)
fi
if [ -z "$LE_EMAIL" ]; then
  echo "LetsEncrypt email is required." >&2
  exit 1
fi

EXTRA=""
if [ "__FORCE__" = "1" ]; then
  EXTRA="--force-renewal"
fi

docker compose -f docker-compose.yml --env-file .env run --rm --entrypoint certbot certbot certonly \
  --webroot -w /var/www/certbot \
  -d "$PUBLIC_HOST" --cert-name "$PUBLIC_HOST" \
  --email "$LE_EMAIL" --agree-tos --no-eff-email \
  --non-interactive $EXTRA

docker compose -f docker-compose.yml --env-file .env restart public
docker exec deploy-public-1 nginx -t

echo "Done renewing $PUBLIC_HOST" >&2
'@

$cmd = $cmd.
  Replace('__EXISTING_REMOTE_DIR__', $ExistingRemoteDir).
  Replace('__PUBLIC_HOST__', $PublicHost).
  Replace('__LE_EMAIL__', $LetsEncryptEmail).
  Replace('__FORCE__', ($(if ($Force) { '1' } else { '0' })))

function New-TempFilePath([string]$prefix, [string]$suffix) {
  $name = "$prefix$([Guid]::NewGuid().ToString('N'))$suffix"
  return (Join-Path $env:TEMP $name)
}

$remoteCmdsPath = New-TempFilePath "aztectrials-renew-" ".txt"
try {
  $cmdFile = @(
    "set -e",
    "cat > $remoteBashPath <<'EOF_AZTECTRIALS'",
    $cmd,
    "EOF_AZTECTRIALS",
    "bash $remoteBashPath",
    "rm -f $remoteBashPath"
  ) -join "`n"

  $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
  [System.IO.File]::WriteAllText($remoteCmdsPath, $cmdFile, $utf8NoBom)

  & $plink -batch -pw $Password -m $remoteCmdsPath "$User@$HostIp"
  if ($LASTEXITCODE -ne 0) { throw "plink failed with exit code $LASTEXITCODE" }
} finally {
  if (Test-Path $remoteCmdsPath) { Remove-Item $remoteCmdsPath -Force -ErrorAction SilentlyContinue }
}