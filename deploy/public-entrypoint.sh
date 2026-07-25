#!/bin/sh
set -eu

HOST="${BUGGYPYRAMID_PUBLIC_HOST:-localhost}"

_pick_le_live_dir() {
  # Certbot-managed certs always have a renewal config.
  # Use that to pick the correct cert name, avoiding stale self-signed "live" dirs.
  if [ -s "/etc/letsencrypt/renewal/${HOST}.conf" ]; then
    echo "/etc/letsencrypt/live/${HOST}"
    return 0
  fi

  newest_name=""
  for conf in /etc/letsencrypt/renewal/${HOST}-*.conf; do
    if [ -f "$conf" ]; then
      newest_name=$(basename "$conf" .conf)
    fi
  done
  if [ -n "$newest_name" ]; then
    echo "/etc/letsencrypt/live/${newest_name}"
    return 0
  fi

  return 1
}

LE_LIVE_DIR=""
if LE_LIVE_DIR=$(_pick_le_live_dir 2>/dev/null); then
  :
else
  LE_LIVE_DIR=""
fi

LE_CERT_FILE="${LE_LIVE_DIR}/fullchain.pem"
LE_KEY_FILE="${LE_LIVE_DIR}/privkey.pem"

SELF_DIR="/etc/nginx/selfsigned/${HOST}"
SELF_CERT_FILE="${SELF_DIR}/fullchain.pem"
SELF_KEY_FILE="${SELF_DIR}/privkey.pem"

TEMPLATE_FILE="/etc/nginx/templates/public.conf.template"
OUT_FILE="/etc/nginx/conf.d/default.conf"

if [ -f "$TEMPLATE_FILE" ]; then
  echo "[public] Rendering nginx config from template for host ${HOST}" >&2

  export BUGGYPYRAMID_PUBLIC_HOST="$HOST"

  # Prefer a real LetsEncrypt cert if present.
  if [ -n "$LE_LIVE_DIR" ] && [ -f "$LE_CERT_FILE" ] && [ -f "$LE_KEY_FILE" ]; then
    export BUGGYPYRAMID_SSL_CERT_FILE="$LE_CERT_FILE"
    export BUGGYPYRAMID_SSL_KEY_FILE="$LE_KEY_FILE"
    echo "[public] Using LetsEncrypt cert: $BUGGYPYRAMID_SSL_CERT_FILE" >&2
  else
    export BUGGYPYRAMID_SSL_CERT_FILE="$SELF_CERT_FILE"
    export BUGGYPYRAMID_SSL_KEY_FILE="$SELF_KEY_FILE"
    echo "[public] Using self-signed cert: $BUGGYPYRAMID_SSL_CERT_FILE" >&2
  fi

  envsubst '${BUGGYPYRAMID_PUBLIC_HOST} ${BUGGYPYRAMID_SSL_CERT_FILE} ${BUGGYPYRAMID_SSL_KEY_FILE}' < "$TEMPLATE_FILE" > "$OUT_FILE"
fi

# Nginx will fail to start if the certificate files don't exist.
# Generate a short-lived self-signed certificate as a bootstrap (NOT in /etc/letsencrypt).
if [ -n "${BUGGYPYRAMID_SSL_CERT_FILE:-}" ] && [ -n "${BUGGYPYRAMID_SSL_KEY_FILE:-}" ]; then
  CERT_FILE="$BUGGYPYRAMID_SSL_CERT_FILE"
  KEY_FILE="$BUGGYPYRAMID_SSL_KEY_FILE"
else
  CERT_FILE="$SELF_CERT_FILE"
  KEY_FILE="$SELF_KEY_FILE"
fi

if [ ! -f "$CERT_FILE" ] || [ ! -f "$KEY_FILE" ]; then
  echo "[public] No valid TLS cert found for ${HOST}; generating temporary self-signed cert." >&2
  mkdir -p "$(dirname "$CERT_FILE")"
  mkdir -p "$(dirname "$KEY_FILE")"
  openssl req -x509 -nodes -newkey rsa:2048 -days 1 \
    -keyout "$KEY_FILE" \
    -out "$CERT_FILE" \
    -subj "/CN=${HOST}" >/dev/null 2>&1 || true
fi

exec "$@"
