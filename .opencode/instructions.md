# AutoApiEngine — Main Agent Instructions

> ⚠️ **HARD RULE: Always create a new branch before making any code changes. Never modify master or develop directly.** ⚠️

This is the **orchestrator agent** for the AutoApiEngine (AutoCrud_Full) project.
Your job is to understand the task and route it to the right sub-agent.

## Repository Structure

The repo has 3 independent working directories:

| Directory | Tech | Entry Point |
|-----------|------|-------------|
| `FE/` | Angular 21 (npm scripts) | `npm start` -> http://localhost:4200 |
| `BE/` | .NET 10 (C#) | `dotnet run --project BE/AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj --launch-profile https` -> https://localhost:7002 |
| `DockerImages/` | Docker Compose | `docker compose up -d` (Keycloak :8081, MySQL :3307, SQL Server :1433) |

## Routing — When to Use Which Agent

| If the task is about... | Use agent |
|-------------------------|-----------|
| Angular components, routes, SSR, guards, styles, HTTP calls | `@frontend` |
| .NET controllers, services, EF Core, migrations, API endpoints, auth | `@backend` |
| Docker Compose, containers, Keycloak, MySQL, SQL Server | `@docker` |
| Code review, PR review, architecture audit, security check | `@review` |
| Git branching, committing, merging, creating PRs | `@merge-push` |

## Mode of Operation (applies to ALL agents)

1. **Always start with analysis** — read the relevant reference docs before coding.
2. **Present implementation options** — offer 2-3 approaches with tradeoffs before coding.
3. **Show steps** — break work into clear step-by-step plan naming files involved.
4. **Branch first** — never modify main/develop directly.
5. **Verify after every change** — run build commands to ensure zero errors.

## Cross-Cutting Gotchas

- opencode.json previously had `"edit": "deny"` — agent can now edit files.
- Backend `KeyClock` section name typo is intentional — do NOT fix it.
- Only `DatabaseProvider=SqlServer` works; MySql is stubbed to throw.
- `FE/src/environments/environment.ts` targets https://localhost:7002/api.
- `DockerImages/docker-compose.yml` has plaintext dev credentials — keep out of logs.
- No CI workflows exist yet (`.github/workflows/` is empty).
- Two EF migration files are gitignored (InitialCreate).

## Reference Docs (supplemental context)

- `ARCHITECTURE.md` — layer diagram, dependency flow, tech choices
- `BE_REFERENCE.md` — all endpoints, DTOs, service listing, startup flow
- `FE_REFERENCE.md` — component tree, route table, guards, incomplete areas
- `DATA_MODEL.md` — entity schema, enums, relationships
- `WORKSPACE_FEATURE.md` — workspace CRUD flow, known gaps

## Quickstart

1. `DockerImages/` -> `docker compose up -d` (start Keycloak + MySQL + SQL Server)
2. `BE/` -> `dotnet run --project BE/AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj --launch-profile https`
3. `FE/` -> `npm install && npm start`
