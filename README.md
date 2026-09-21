# Warp Bay Auto Lab — BAY-07 Scheduling

Fictional neon-synthwave auto shop scheduler. **All names, plates, phones are invented demo data.**
No affiliation with any real shop, employer, or portfolio client.

## Why this exists (portfolio)

New stack vs johndyates.com current site: **C# .NET 8, EF Core, SQL Server, JWT/RBAC,
xUnit, Docker, SignalR, background workers** — none on the live portfolio today.
Pairs with Deadwax (Node marketplace) to show backend range.

Common SE requirements demonstrated:
- JWT + RBAC (Admin/Manager/Tech/Customer), role-gated state machine
- CRUD + pagination/filter/search, FluentValidation-style input guard
- Concurrency: bay/tech overlap guard → **409**, `RowVersion` token, idempotency keys
- Audit trail (`StatusHistory`), health checks, Serilog logging, Swagger/OpenAPI
- Realtime: SignalR `schedule-changed` hub; reminders via `IHostedService` worker
- Seed + one-click demo logins + `/api/demo/reset` (Deadwax pattern)

## Quickstart (zero-setup demo)

```bash
# API (SQLite file, seeds on first run)
dotnet run --project WarpBay.Api --urls http://localhost:5123
# Client
cd warp-bay-client && npm install && npm run dev   # VITE_API_URL=http://localhost:5123
```

Open client → **Login tab → Login as Manager** (one click), or:
- `admin@warp-bay.demo / Demo123!`
- `manager@warp-bay.demo / Demo123!`
- `tech@warp-bay.demo / Demo123!`
- `customer@warp-bay.demo / Demo123!`

Try the 409 path: book with plate `DEMO-409`, or double-book the same bay/slot.
Reset anytime: `POST /api/demo/reset`.

## SQL Server (resume keyword) via Docker

```bash
docker compose up --build   # api :5123, sqlserver :1433 (sa/WarpBay123!)
```

`Provider` switch in appsettings: `Sqlite` (default) | `SqlServer` | `Npgsql` (Render Postgres).

## API surface

```
POST /api/auth/login | POST /api/auth/demo | GET /api/me
GET  /api/services /api/bays /api/techs
GET  /api/availability?serviceId=&date=yyyy-MM-dd
POST /api/appointments (+ Idempotency-Key header)
GET  /api/appointments?day&tech&status&q&page&pageSize   (auth)
PATCH /api/appointments/{id}/status {to}                (role-gated)
GET  /api/admin/reports/day?date=                        (Manager/Admin)
GET  /api/demo/credentials | POST /api/demo/reset
GET  /health | /swagger | SignalR /hubs/schedule
```

## Tests

```bash
dotnet test WarpBay.sln   # 9 tests: state-machine matrix, double-book 409, idempotency, demo-409
```

## Brand assets (generated, checked in)

- `warp-bay-client/public/logo.svg` — comet + BAY-07 badge (custom, no stock)
- `warp-bay-client/public/favicon.svg` — mono mark
- Avatars: deterministic hue circles (no photos, no likeness issues)
- Palette: `#0b0620` void, `#22d3ee` cyan, `#f472b6` magenta, `#818cf8` indigo

## Deploy notes

- API: Render/Fly (`Npgsql` provider + `DATABASE_URL`), or Azure App Service for the keyword
- Client: GitHub Pages / Netlify with `VITE_API_URL` pointed at the API
