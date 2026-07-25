# BuggyPyramid Admin Web App

Minimal Node.js + React admin UI for the Go server in `Server`.

## Run (dev)

1) Run the Go server (default `http://localhost:8080`).

2) In this folder:

- `npm install`
- `npm run dev`

Open `http://localhost:5173`.

## API connectivity

During development, Vite proxies `/admin/*` to `http://localhost:8080` (see `vite.config.ts`).

In the UI:

- Open **Settings** and set **Admin Key** to your `BUGGYPYRAMID_ADMIN_KEY` value (or the fallback constant).

## Supported endpoints

- Logs: `GET /admin/logs`, `GET /admin/logs/{id}`, `POST /admin/logs/clear`
- Players: `GET /admin/players` (supports `q`, `order`, `page`, `pageSize`),
  `GET /admin/players/{playername}`, `DELETE /admin/players/{playername}`, `POST /admin/players/clear`
- Rate limits: `GET /admin/ratelimits` (supports `ip`, `endpoint`, `event`, `q`, `page`, `pageSize`),
  `POST /admin/ratelimits/clear`
