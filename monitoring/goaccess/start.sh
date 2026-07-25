#!/bin/sh
set -eu

REPORT=/var/www/goaccess/index.html
CONFIG=/config/goaccess.conf
ACCESS_LOG=/logs/access.log

mkdir -p /var/www/goaccess
touch "$REPORT"

httpd -f -p 8080 -h /var/www/goaccess &
HTTP_PID=$!
GOACCESS_PID=""

cleanup() {
  if [ -n "$GOACCESS_PID" ]; then
    kill -TERM "$GOACCESS_PID" 2>/dev/null || true
    wait "$GOACCESS_PID" 2>/dev/null || true
  fi
  kill -TERM "$HTTP_PID" 2>/dev/null || true
  wait "$HTTP_PID" 2>/dev/null || true
}
trap cleanup INT TERM EXIT

stream_logs() {
  # logrotate suffixes increase with age, so reverse version order is oldest first.
  find /logs -maxdepth 1 -type f -name 'access.log.*.gz' -print \
    | sort -Vr \
    | while IFS= read -r archive; do zcat "$archive"; done
  find /logs -maxdepth 1 -type f -name 'access.log.[0-9]*' ! -name '*.gz' -print \
    | sort -Vr \
    | while IFS= read -r archive; do cat "$archive"; done
  tail -n +1 -F "$ACCESS_LOG"
}

stream_logs | goaccess - \
  --no-global-config \
  --config-file="$CONFIG" \
  --output="$REPORT" \
  --real-time-html \
  --addr=0.0.0.0 \
  --port=7891 \
  --ws-url="$GOACCESS_WS_URL" \
  --origin="$GOACCESS_ORIGIN" \
  --ping-interval=30 &
GOACCESS_PID=$!

while kill -0 "$HTTP_PID" 2>/dev/null && kill -0 "$GOACCESS_PID" 2>/dev/null; do
  sleep 5
done

exit 1
