# AutoApiEngine Agent Routing

This file is a **quick-reference routing table**. Sub-agents contain detailed instructions.
The main agent instructions are in `.opencode/instructions.md`.

## Sub-Agents

| Agent | `@` name | Purpose | Instruction File |
|-------|----------|---------|-----------------|
| **Frontend** | `@frontend` | Angular 21 components, SSR, guards, routing, HTTP | `.opencode/agents/frontend.md` |
| **Backend** | `@backend` | .NET 10 controllers, EF Core, services, API, auth | `.opencode/agents/backend.md` |
| **Docker** | `@docker` | Compose, Keycloak, MySQL, SQL Server | `.opencode/agents/docker.md` |
| **Review** | `@review` | Code review, security audit, PR review (read-only) | `.opencode/agents/review.md` |
| **Merge/Push** | `@merge-push` | Branch, commit, push, merge, PR | `.opencode/agents/merge-push.md` |

## Quickstart

```powershell
# Infrastructure
cd DockerImages; docker compose up -d

# Backend (HTTPS)
cd BE; dotnet run --project AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj --launch-profile https

# Frontend
cd FE; npm install; npm start
```

## Ports

- Frontend: http://localhost:4200
- Backend API: https://localhost:7002 (http://localhost:5145)
- Keycloak: http://localhost:8081
- SQL Server: localhost:1433
- MySQL: localhost:3307

## Reference Docs

- `ARCHITECTURE.md` — layer diagram, dependency flow
- `BE_REFERENCE.md` — all endpoints, DTOs, service listing
- `FE_REFERENCE.md` — component tree, route table, guards
- `DATA_MODEL.md` — entity schema, enums, relationships
- `WORKSPACE_FEATURE.md` — workspace CRUD flow, known gaps

## Verification Commands

- Backend: `dotnet build BE/AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj`
- Frontend: `npm run build` from `FE/`
- Tests: `npm test` (FE) or `dotnet test` (BE)
