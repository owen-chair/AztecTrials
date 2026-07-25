#!/usr/bin/env python3
"""Persist and normalize deploy-public-1 access records without Docker socket exposure."""

from __future__ import annotations

import ipaddress
import os
from pathlib import Path
import re
import subprocess
import sys
import time

CONTAINER = os.environ.get("SOURCE_CONTAINER", "deploy-public-1")
OUTPUT = Path(os.environ.get("OUTPUT_LOG", "/var/lib/goaccess-monitor/logs/access.log"))
CURSOR = Path(os.environ.get("CURSOR_FILE", "/var/lib/goaccess-monitor/collector.cursor"))
IGNORE_IPS = {
    value.strip()
    for value in os.environ.get("IGNORE_IPS", "192.168.178.27").split(",")
    if value.strip()
}

TIMESTAMPED_LINE = re.compile(r"^(?P<timestamp>\S+) (?P<message>.*)$")
ACCESS_LINE = re.compile(
    r'^(?P<ip>\S+) - (?P<user>\S+) '
    r'(?P<body>\[[^]]+\] "[^"]*" \d{3} \S+ "[^"]*" "[^"]*" "[^"]*")$'
)
REQUEST = re.compile(r'^\S+ - \S+ \[[^]]+\] "\S+ (?P<url>\S+) [^"]+" ')


def ignored_source(value: str) -> bool:
    if value in IGNORE_IPS:
        return True
    try:
        address = ipaddress.ip_address(value)
    except ValueError:
        return False
    if address.is_loopback:
        return True
    return isinstance(address, ipaddress.IPv4Address) and address in ipaddress.ip_network(
        "172.16.0.0/12"
    )


def normalize(message: str) -> str | None:
    match = ACCESS_LINE.match(message)
    if not match or ignored_source(match.group("ip")):
        return None

    request = REQUEST.match(message)
    if request and request.group("url").split("?", 1)[0] == "/_diag/ping":
        return None

    # The current nginx format has no vhost, TLS, or timing fields. Keep explicit
    # placeholders so this spool already matches the future native nginx format.
    return f"legacy-unknown {message} UNKNOWN 0.000\n"


def save_cursor(timestamp: str) -> None:
    temporary = CURSOR.with_suffix(".tmp")
    temporary.write_text(timestamp + "\n", encoding="ascii")
    os.replace(temporary, CURSOR)


def main() -> int:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    CURSOR.parent.mkdir(parents=True, exist_ok=True)
    resume_at = CURSOR.read_text(encoding="ascii").strip() if CURSOR.exists() else ""

    command = ["/usr/bin/docker", "logs", "--timestamps", "--follow"]
    if resume_at:
        command.extend(["--since", resume_at])
    command.append(CONTAINER)

    process = subprocess.Popen(
        command,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        bufsize=1,
    )
    assert process.stdout is not None

    latest_timestamp = resume_at
    last_checkpoint = time.monotonic()
    with OUTPUT.open("a", encoding="utf-8", buffering=1) as output:
        for raw_line in process.stdout:
            parsed = TIMESTAMPED_LINE.match(raw_line.rstrip("\n"))
            if not parsed:
                continue
            timestamp = parsed.group("timestamp")
            if resume_at and timestamp <= resume_at:
                continue
            normalized = normalize(parsed.group("message"))
            if normalized:
                output.write(normalized)
            latest_timestamp = timestamp
            now = time.monotonic()
            if now - last_checkpoint >= 1:
                save_cursor(latest_timestamp)
                last_checkpoint = now

    if latest_timestamp:
        save_cursor(latest_timestamp)

    return process.wait()


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"goaccess collector failed: {error}", file=sys.stderr)
        raise
