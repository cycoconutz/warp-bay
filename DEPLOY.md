# Deploying Warp Bay Auto Lab

Target: **API on Render**, **client on GitHub Pages**, linked from the portfolio catalog.

Repo layout (this folder is a standalone git repo):

```
warp-bay/
  WarpBay.Api/            .NET 8 API  (Render)
  WarpBay.Tests/          xUnit tests
  warp-bay-client/        React + Vite client (GitHub Pages)
  Dockerfile              API image
  render.yaml             Render Blueprint
  .github/workflows/pages.yml
```

## 0. One-time: authenticate

```bash
gh auth login          # GitHub CLI is installed but not signed in
git config user.name   # must be set for commits (repo-local is fine)
git config user.email
```

## 1. Create the repository and push

From this `warp-bay/` folder:

```bash
git add -A
git commit -m "Warp Bay Auto Lab: .NET 8 scheduling API + React client"
gh repo create cycoconutz/warp-bay --public --source=. --remote=origin --push
```

## 2. Deploy the API on Render

1. Render dashboard -> **New -> Blueprint**.
2. Connect `cycoconutz/warp-bay`. Render reads `render.yaml` and creates `warp-bay-api`.
3. When prompted, set **`Jwt__Key`** to a long random string (e.g. `openssl rand -hex 32`).
4. Deploy. Verify:
   - `https://warp-bay-api.onrender.com/health` -> `Healthy`
   - `https://warp-bay-api.onrender.com/swagger`
   - `https://warp-bay-api.onrender.com/api/demo/credentials`

Notes
- `Provider=Sqlite` keeps the demo dependency-free; the DB reseeds on a fresh instance.
- For persistence, switch `Provider` to `Npgsql` and set `ConnectionStrings__Npgsql` (Neon free tier works well).
- Free instances sleep after ~15 min; the first request may take ~30-60s to wake, then loads fast.

## 3. Point the client at the API and publish to Pages

1. Repo **Settings -> Secrets and variables -> Actions -> Variables** -> add
   `VITE_API_URL = https://warp-bay-api.onrender.com`
2. Repo **Settings -> Pages -> Source: GitHub Actions**.
3. Re-run the `Deploy warp-bay client to GitHub Pages` workflow (or push a change under `warp-bay-client/`).
4. The client goes live at `https://cycoconutz.github.io/warp-bay/`.

The workflow writes `.env` from the `VITE_API_URL` variable at build time; the Vite
`base` is `/warp-bay/` so assets resolve under the project path.

## 4. Update the portfolio catalog

In the `portfolio-tabs` repo, the Warp Bay entry is already added with:

- theme `warpbay` (`src/theme/themes.css`)
- wipe `warp` (`src/transitions/wipes.ts`, styles in `src/index.css`)
- screenshot `src/assets/shots/warpbay.png`
- entry `06 · Warp Bay Auto Lab` (`src/data/projects.ts`)

Commit and push to trigger the existing Pages deploy:

```bash
cd portfolio-tabs
git add -A
git commit -m "Add Warp Bay Auto Lab to the catalog"
git push
```

## Local dev

```bash
dotnet run --project WarpBay.Api --urls http://localhost:5123
cd warp-bay-client && npm install && npm run dev
```

Demo logins (one click in the UI, or `POST /api/auth/demo`):
`admin|manager|tech|customer @warp-bay.demo` / `Demo123!`
