# AutoApiEngine Dev Infra (DockerImages/)

Compose file lives at `DockerImages/docker-compose.yml` (not repo root).

## Commands (run from `DockerImages/`)
- Start: `docker compose up -d`
- Rebuild images: `docker compose up -d --build`
- Stop: `docker compose down`
- Stop + remove volumes: `docker compose down -v`

## Services / Ports (host -> container)
- `keycloak`: `8081:8080` (built from `KeyClock/`; runs `start-dev --import-realm -Dkeycloak.theme.default=apiengine`; realm+theme are bind-mounted).
- `mysql`: `3307:3306` (Keycloak DB; built from `mysql/`; init script is `mysql/init.sql`; volume `mysql_data`).
- `sqlserver`: `1433:1433` (app DB; built from `sqlserver/`; volume `sqlserver_data`).

## Gotchas
- `docker-compose.yml` contains plaintext dev credentials (Keycloak admin, DB passwords, SMTP password); treat as sensitive and don’t paste into tickets/logs.
- The `dotnet-app` service is commented out; `back-end-app/Dockerfile` is outdated (targets .NET 8 and doesn’t match current BE paths/entrypoint).
