#!/usr/bin/env bash
set -euo pipefail

# Minimal Ubuntu setup for Docker + small swap.
# Run as root.

SWAP_MB=20

apt-get update
apt-get install -y ca-certificates curl gnupg
install -m 0755 -d /etc/apt/keyrings

if [ ! -f /etc/apt/keyrings/docker.gpg ]; then
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
  chmod a+r /etc/apt/keyrings/docker.gpg
fi

ARCH="$(dpkg --print-architecture)"
CODENAME="$(. /etc/os-release; echo "$VERSION_CODENAME")"
echo "deb [arch=${ARCH} signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu ${CODENAME} stable" \
  > /etc/apt/sources.list.d/docker.list

apt-get update
DOCKER_CE_CANDIDATE="$(apt-cache policy docker-ce | awk '/Candidate:/ { print $2 }')"
if [ -n "$DOCKER_CE_CANDIDATE" ] && [ "$DOCKER_CE_CANDIDATE" != "(none)" ]; then
  if dpkg -s docker-compose-v2 >/dev/null 2>&1; then
    apt-get remove -y docker-compose-v2
  fi
  apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
else
  apt-get install -y docker.io docker-compose-v2
fi
systemctl enable --now docker

# Configure log rotation so Docker can't grow logs without bounds.
mkdir -p /etc/docker
cat > /etc/docker/daemon.json <<'JSON'
{
  "log-driver": "json-file",
  "log-opts": {
    "max-size": "10m",
    "max-file": "3"
  }
}
JSON
systemctl restart docker

# Small swap file (if no swap exists). This is intentionally tiny.
if ! swapon --show | grep -q '^'; then
  if [ ! -f /swapfile ]; then
    fallocate -l "${SWAP_MB}M" /swapfile || dd if=/dev/zero of=/swapfile bs=1M count=${SWAP_MB}
    chmod 600 /swapfile
    mkswap /swapfile
  fi
  swapon /swapfile
  if ! grep -q '^/swapfile ' /etc/fstab; then
    echo '/swapfile none swap sw 0 0' >> /etc/fstab
  fi
fi

# Mild swappiness so we only swap under pressure.
sysctl -w vm.swappiness=10
if ! grep -q '^vm.swappiness=' /etc/sysctl.conf; then
  echo 'vm.swappiness=10' >> /etc/sysctl.conf
fi

echo "Docker installed and swap configured."
