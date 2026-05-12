# AutoApiEngine (AutoCrud_Full)

This repo is 3 separate working dirs; run commands from the right one:
- `FE/` (Angular 21 app, npm scripts live here)
- `BE/` (.NET backend; entrypoint is `BE/AutoApiEngine.ApiServices/`)
- `DockerImages/` (dev infra via `docker-compose.yml`)

Sub-guides (treat as authoritative per area):
- `FE/AGENTS.md`
- `BE/AGENTS.md`
- `DockerImages/AGENTS.md`

**Quickstart (local)**
1. Infra: from `DockerImages/` run `docker compose up -d` (Keycloak + DBs).
2. Backend (HTTPS): `dotnet run --project BE/AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj --launch-profile https`.
3. Frontend: from `FE/` run `npm install` then `npm start`.

**Ports / URLs (defaults in config)**
- Frontend dev: `http://localhost:4200`
- Backend API: `https://localhost:7002` (also `http://localhost:5145` via launch profile)
- Keycloak: `http://localhost:8081`
- SQL Server (Docker): `localhost:1433`
- MySQL (Docker, Keycloak DB): `localhost:3307`

**Cross-Cutting Gotchas**
- FE `src/environments/environment.ts` targets `https://localhost:7002/api`; run the backend with the `https` launch profile unless you change the FE env.
- Backend targets `net10.0` (requires a .NET 10 SDK).
- Backend config section name is `KeyClock` (typo) in `BE/AutoApiEngine.ApiServices/appsettings.json`.
- Backend only supports `DatabaseProvider=SqlServer`; `MySql` is wired to throw in `Providers/DataBaseProvider.cs`.
- `DockerImages/docker-compose.yml` hardcodes dev credentials (DB + Keycloak + SMTP); avoid pasting them into issues/logs.
