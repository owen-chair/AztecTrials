# Standalone GoAccess Monitor

This project monitors the existing `deploy-public-1` nginx logs without joining its networks, mounting the Docker socket into a container, changing nginx, or operating any existing Compose project.

## Directory Structure

```text
monitoring/goaccess/
|-- docker-compose.yml
|-- Dockerfile
|-- goaccess.conf
|-- start.sh
|-- collector.py
|-- goaccess-collector.service
|-- logrotate.conf
|-- deploy-goaccess.ps1
|-- .env.example
`-- maintenance/
    |-- nginx-goaccess-log.conf
    `-- compose-public-mount.example.yml
```

Server-side state is independent:

```text
/opt/goaccess-monitor/                 # standalone Compose/config files
/var/lib/goaccess-monitor/logs/        # normalized persistent access history
/var/lib/goaccess-monitor/html/        # generated dashboard
/var/lib/goaccess-monitor/geoip/       # private GeoLite2 database copy
/etc/systemd/system/goaccess-collector.service
/etc/logrotate.d/goaccess-monitor
```

## Current Data Flow

```text
deploy-public-1 stdout/stderr
    -> docker logs --timestamps --follow (host systemd collector)
    -> /var/lib/goaccess-monitor/logs/access.log
    -> read-only mount in GoAccess
    -> HTML :7890 and WebSocket :7891 on 192.168.178.27 only
```

The collector requests logs through Docker's supported API rather than reading Docker's internal JSON files. On first start it imports all history still retained by the container's three 10 MB `json-file` rotations, then follows new records. Its timestamp cursor prevents a service restart from replaying the full Docker history.

Current nginx logs do not contain virtual host, TLS version, or response time. Historical records therefore use `legacy-unknown`, `UNKNOWN`, and `0.000`. All other panels work now. The prepared native format fills those three fields after a maintenance-window migration.

Crawlers and Internet scanners are retained. Only `/_diag/ping`, loopback, the host's own `192.168.178.27` checks, and Docker bridge sources in `172.16.0.0/12` are discarded.

## Deploy

From the repository root on Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\monitoring\goaccess\deploy-goaccess.ps1
```

The upstream official GoAccess image currently publishes amd64 and arm64 manifests but not the server's ARMv7 architecture. This project therefore builds GoAccess from Alpine's maintained package on the official Alpine 3.20 base. The resulting image contains GoAccess and BusyBox only; it is built solely for this monitoring project.

The script uploads only this monitoring project plus a private copy of the existing GeoLite2 database. Its Docker lifecycle target is exclusively `goaccess-monitor`:

```bash
docker compose -p goaccess-monitor -f /opt/goaccess-monitor/docker-compose.yml build --pull
docker compose -p goaccess-monitor -f /opt/goaccess-monitor/docker-compose.yml up -d --wait --wait-timeout 120
```

It does not run Compose against `deploy`, `aztectrials`, or any other project.

Manual equivalent on the Ubuntu host, after placing the files at `/opt/goaccess-monitor` and GeoIP database at the configured path:

```bash
sudo install -d -m 0755 /var/lib/goaccess-monitor/{logs,geoip}
sudo install -d -m 0755 -o 65532 -g 65532 /var/lib/goaccess-monitor/html
sudo touch /var/lib/goaccess-monitor/logs/access.log
sudo chmod 0644 /var/lib/goaccess-monitor/logs/access.log
sudo install -m 0644 /opt/goaccess-monitor/goaccess-collector.service /etc/systemd/system/goaccess-collector.service
sudo install -m 0644 /opt/goaccess-monitor/logrotate.conf /etc/logrotate.d/goaccess-monitor
sudo systemctl daemon-reload
sudo systemctl enable --now goaccess-collector.service
sudo docker compose -p goaccess-monitor -f /opt/goaccess-monitor/docker-compose.yml build --pull
sudo docker compose -p goaccess-monitor -f /opt/goaccess-monitor/docker-compose.yml up -d --wait --wait-timeout 120
```

Dashboard:

```text
http://192.168.178.27:7890/
```

Port 7890 serves HTML. Port 7891 is the live WebSocket. Both host publications are explicitly bound to `192.168.178.27`; GoAccess listening on `0.0.0.0` inside its private container namespace does not create a wildcard host listener.

## Verification

Confirm the standalone project and health:

```bash
docker compose -p goaccess-monitor -f /opt/goaccess-monitor/docker-compose.yml ps
docker inspect --format '{{.State.Health.Status}}' goaccess-monitor-goaccess-1
curl -fsS http://192.168.178.27:7890/ >/dev/null
```

Confirm LAN-only host bindings and absence of wildcard publications:

```bash
ss -lntp | grep -E '192\.168\.178\.27:(7890|7891)'
ss -lntp | grep -E '(^|[[:space:]])(0\.0\.0\.0|\[::\]):(7890|7891)' && echo 'UNEXPECTED WILDCARD BIND' || echo 'LAN-only bind OK'
docker port goaccess-monitor-goaccess-1
```

From a LAN machine:

```bash
curl -I http://192.168.178.27:7890/
```

From outside the LAN, neither port should route. The definitive host checks are the explicit `ss` and `docker port` outputs above; an Internet check should time out or be refused:

```bash
curl --connect-timeout 5 http://82.71.124.89:7890/
```

Confirm historical and live ingestion:

```bash
systemctl status goaccess-collector.service --no-pager
journalctl -u goaccess-collector.service -n 50 --no-pager
wc -l /var/lib/goaccess-monitor/logs/access.log
tail -n 3 /var/lib/goaccess-monitor/logs/access.log
before=$(wc -l < /var/lib/goaccess-monitor/logs/access.log)
# Make a normal request through either public hostname, then run:
after=$(wc -l < /var/lib/goaccess-monitor/logs/access.log)
printf 'before=%s after=%s\n' "$before" "$after"
```

Confirm production identity and uptime without changing it:

```bash
docker inspect --format '{{.Name}} running={{.State.Running}} started={{.State.StartedAt}}' deploy-public-1
docker ps --filter name=deploy-public-1 --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}'
```

## Persistence And Rotation

The normalized access log is the authoritative history. It is a host bind mount, so it survives GoAccess container recreation and server reboot. GoAccess rebuilds its in-memory database from rotated archives and the current file, avoiding duplicate-prone persistence of piped records.

Host logrotate keeps 30 daily rotations, compresses older files, and uses `copytruncate`, so neither the collector nor nginx needs a signal or reload. Docker rotation is handled by `docker logs`; the collector never opens Docker's private JSON files.

Test the rotation policy without rotating anything:

```bash
logrotate --debug /etc/logrotate.d/goaccess-monitor
```

## Native Nginx Log Migration

No native migration is performed now. The current nginx container has no host mount for `/var/log/nginx/goaccess`. Docker cannot add a bind mount to a running container, so a persistent native log requires recreating that one production container. There is no honest zero-interruption command for adding the mount to the current single-container edge.

The maintenance configuration is prepared at:

```text
/opt/goaccess-monitor/maintenance/nginx-goaccess-log.conf
/opt/goaccess-monitor/maintenance/compose-public-mount.example.yml
```

During an approved maintenance window:

1. Back up the deployed BuggyPyramid Compose file and `nginx-public.conf` template.
2. Add a read-write bind mount from `/var/lib/goaccess-monitor/logs` to `/var/log/nginx/goaccess` on the existing public service.
3. Add the `map` and `log_format` directives from the prepared file at nginx `http` context, before the server blocks.
4. Add the documented `access_log` directive inside every public server block, including both BuggyPyramid blocks and both managed Aztec blocks.
5. Run `docker compose config` against the existing project. This validates YAML only and does not alter containers.
6. Stop `goaccess-collector.service` immediately before the maintenance action so only nginx writes the spool.
7. Use the site's established maintenance procedure to recreate only the public edge with the new mount. This is deliberately not automated or executed by this project because it affects production.
8. Verify nginx, both public hostnames, and the enriched log before ending the window.
9. Leave the collector disabled; GoAccess already follows the same host file and needs no parser change.

After migration, verify fields directly:

```bash
tail -n 3 /var/lib/goaccess-monitor/logs/access.log
```

Each new record should begin with the actual hostname and end with values similar to `TLSv1.3 0.042`. The Virtual Hosts, TLS Type, and response-time columns will then populate for new traffic. Historical records remain clearly grouped under `legacy-unknown`.

## Impact on Existing Production Deployment

The current deployment does not edit any production Compose file, nginx template, network, volume, or container. It does not execute `docker restart`, `docker compose up` for an existing project, `docker exec`, nginx reloads, signals, or network attachment commands.

The host collector performs only the supported read operation `docker logs --timestamps --follow deploy-public-1`. The GoAccess container receives only a read-only normalized log mount and a copied GeoIP database. It has no Docker socket, no production network membership, no Linux capabilities, a read-only root filesystem, `no-new-privileges`, a non-root UID, bounded memory/PIDs, and only two LAN-address-specific port publications.

Starting or stopping this monitoring project cannot stop, restart, recreate, or reconfigure `deploy-public-1`, BuggyPyramid, AztecTrials, their databases, or their admin applications.
