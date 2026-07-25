# AztecTrials Side-by-Side Deployment

This deployment is intentionally separate from the existing BuggyPyramid leaderboard stack.

New public host:

- `https://aztec.blobfishman.xyz/`

The existing deployment keeps owning host ports `80` and `443`. AztecTrials runs its own `aztectrials` Docker Compose project with its own containers, network, and Postgres volume, then installs a small API-only `server_name aztec.blobfishman.xyz` block into the existing edge nginx container.

## What Is Isolated

- Compose project: `aztectrials`
- Remote source directory: `/opt/aztectrials/src`
- Docker networks: `aztectrials_edge`, `aztectrials_backend`, `aztectrials_admin_access`
- Postgres volume: `aztectrials_postgres_data`
- Images: `aztectrials/server:local`, `aztectrials/admin:local`
- Database name/user defaults: `aztectrials`
- Admin web app: privately bound to `192.168.178.27:8082` through `aztectrials_admin_access`; it is not attached to the public edge network

The server still reads `BUGGYPYRAMID_*` environment variable names because the application code was duplicated from the original leaderboard server. Keep those env var names for compatibility; the values are AztecTrials-specific.

## DNS

Create an A record:

- `aztec.blobfishman.xyz` -> `82.71.124.89`

LetsEncrypt HTTP-01 validation uses the existing edge nginx container, so inbound TCP `80` and `443` must continue to reach the VPS.

## Deploy From Windows

The deployment script reads `deploy/credentials.txt` when `-HostIp` and `-Password` are not passed. The expected two-line format is:

```text
<vps-ip-or-host>
<ssh-password>
```

Run from the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\deploy-to-vps.ps1
```

The script verifies DNS before touching the edge nginx config. If DNS is not ready yet, deploy only the isolated containers with:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\deploy-to-vps.ps1 -SkipEdgeInstall
```

Then create the DNS record and rerun the normal deploy command to install the public HTTPS route.

Useful optional parameters:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\deploy-to-vps.ps1 `
  -PublicHost aztec.blobfishman.xyz `
  -AdminBindIp 192.168.178.27 `
  -AdminPort 8082 `
  -LetsEncryptEmail you@example.com
```

The script will:

- upload only `Server`, `AdminWebApp`, and `deploy`
- exclude local `.env`, credentials, backups, `node_modules`, and build output
- deploy to `/opt/aztectrials/src`
- run `docker compose -p aztectrials up -d --build`
- connect the existing `deploy-public-1` edge nginx container to `aztectrials_edge`
- issue/reuse a LetsEncrypt cert for `aztec.blobfishman.xyz`
- append/replace only the managed AztecTrials nginx block in the existing edge template
- validate nginx and roll back the edge template if validation fails
- verify the new API, private admin binding, public non-API rejection, and existing deployment diagnostic endpoint

## URLs

Admin Web App:

- `http://192.168.178.27:8082/`

The Admin Web App is bound only to the private host address. It is not proxied by the public edge nginx and is not available through `aztec.blobfishman.xyz`.

Public API base:

- `https://aztec.blobfishman.xyz`

Allowed public API paths:

- `/time/submit/...`
- `/data/top10/...`
- `/data/top100/...`
- `/data/page/...`
- `/data/personal/...`
- `/metrics/checkpointUnlock/...`
- `/metrics/genericMetric/...`

All other public paths return `404`; the HTTPS root closes with nginx status `444`, matching the existing public deployment. In particular, `/admin` and `/admin/` are not publicly exposed.

Do not use `:8080` in VRChat or Unity URLs. Port `8080` is internal to Docker.

## Admin Key

The first deploy creates `/opt/aztectrials/src/deploy/.env` with a generated `BUGGYPYRAMID_ADMIN_KEY` if one does not already exist.

To read or rotate it on the VPS:

```bash
cd /opt/aztectrials/src/deploy
sudo nano .env
docker compose -p aztectrials -f docker-compose.yml --env-file .env up -d
```

## Unity Project

Do not change the Unity project as part of deployment.

When you are ready to point Unity at the new leaderboard, update the leaderboard base URL/config in Unity from the existing domain to:

```text
https://aztec.blobfishman.xyz
```

The endpoint paths and request payload format are unchanged. The public client key is still the same constant used by the duplicated server unless you intentionally change it in `Server/Server.go` and the Unity project together.