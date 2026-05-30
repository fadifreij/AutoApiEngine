# AutoApiEngine Dev Infra (DockerImages/)

Run `docker compose` commands from `DockerImages/`.

## Commands

| Command | Purpose |
|---------|---------|
| `docker compose up -d` | Start all services |
| `docker compose up -d --build` | Rebuild and start |
| `docker compose down` | Stop services |
| `docker compose down -v` | Stop + remove volumes (data loss!) |

## Services

| Service | Port | Notes |
|---------|------|-------|
| keycloak | 8081 | Built from `KeyClock/`; `start-dev --import-realm`; theme `apiengine` |
| mysql | 3307 | Keycloak DB; init script `mysql/init.sql`; volume `mysql_data` |
| sqlserver | 1433 | App DB; volume `sqlserver_data` |

## Gotchas

- Plaintext dev credentials in `docker-compose.yml` — keep out of logs.
- `dotnet-app` service commented out; its Dockerfile targets .NET 8 (wrong).
- MySQL provider in BE is stubbed — MySQL container is for Keycloak only.
- Realm config is bind-mounted (not baked).

## Detailed Reference

See `.opencode/agents/docker.md` for the full sub-agent instructions.
