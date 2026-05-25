# Mode of Operation

## Always start with analysis
Before any task, thoroughly read and understand the relevant parts of the codebase. Consult ARCHITECTURE.md, BE_REFERENCE.md, FE_REFERENCE.md, DATA_MODEL.md, and WORKSPACE_FEATURE.md. Demonstrate understanding of the full stack (DB -> API -> Services -> UI) and follow existing patterns and conventions.

## Present implementation options
For every task, present 2-3 implementation approaches with clear tradeoffs. Explain which you recommend and why. Do not jump straight into coding.

## Show steps
Always break work into a clear step-by-step plan before executing. Each step must name the files involved and the expected outcome. Mark steps as completed as you go.

---
# AutoApiEngine (AutoCrud_Full)

This repo is 3 separate working dirs; run commands from the right one:
- FE/ (Angular 21 app, npm scripts live here)
- BE/ (.NET backend; entrypoint is BE/AutoApiEngine.ApiServices/)
- DockerImages/ (dev infra via docker-compose.yml)

Sub-guides (treat as authoritative per area):
- FE/AGENTS.md
- BE/AGENTS.md
- DockerImages/AGENTS.md

Detailed reference docs (supplemental context):
- ARCHITECTURE.md â€” layer diagram, dependency flow, tech choices
- BE_REFERENCE.md â€” all endpoints, DTOs, service listing, startup flow
- FE_REFERENCE.md â€” component tree, route table, guards, incomplete areas
- DATA_MODEL.md â€” entity schema, enums, relationships
- WORKSPACE_FEATURE.md â€” workspace CRUD flow, known gaps

**Quickstart (local)**
1. Infra: from DockerImages/ run docker compose up -d (Keycloak + MySQLâ€”SQL Server is commented out in compose; run locally or uncomment).
2. Backend (HTTPS): dotnet run --project BE/AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj --launch-profile https.
3. Frontend: from FE/ run npm install then npm start.

**Ports / URLs (defaults in config)**
- Frontend dev: http://localhost:4200
- Backend API: https://localhost:7002 (also http://localhost:5145 via launch profile)
- Keycloak: http://localhost:8081
- SQL Server (Docker): localhost:1433
- MySQL (Docker, Keycloak DB): localhost:3307

**Tooling Constraints**
- opencode.json has "edit": "deny" â€” OpenCode can read/search/bash but cannot write or edit files.
- No CI workflows exist yet (.github/workflows/ is empty).

**Cross-Cutting Gotchas**
- FE src/environments/environment.ts targets https://localhost:7002/api; run the backend with the https launch profile unless you change the FE env.
- Backend targets net10.0 (requires a .NET 10 SDK).
- Backend config section name is KeyClock (typo) in BE/AutoApiEngine.ApiServices/appsettings.json.
- Backend only supports DatabaseProvider=SqlServer; MySql is wired to throw in Providers/DataBaseProvider.cs.
- DockerImages/docker-compose.yml hardcodes dev credentials (DB + Keycloak + SMTP); avoid pasting them into issues/logs.
- .gitignore explicitly ignores the two existing EF migration files (InitialCreate).
