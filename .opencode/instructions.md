# AutoApiEngine — Main Agent Instructions

> ✅ **Working policy: all changes are committed directly on `master`. No feature branches unless explicitly requested.**

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
| Git commits, pushes, PRs | `@merge-push` |

## Skills

Load with the `skill` tool. Skills are read-only workflows — they explain, they
do not edit code.

| Skill | Use when the user asks to... |
|-------|-------------------------------|
| `easy-review` | review anything (code, diff, feature, architecture), find gaps, or hear ideas/thoughts. Explains findings in plain English with real before/after examples and writes a report to `doc/reviews/` |

## Mode of Operation (applies to ALL agents)

1. **Always start with analysis** — read the relevant reference docs before coding.
2. **Present implementation options** — offer 2-3 approaches with tradeoffs before coding.
3. **Show steps** — break work into clear step-by-step plan naming files involved.
4. **Commit directly to `master`** — no feature branches.
5. **Verify after every change** — run build commands to ensure zero errors.

## Cross-Cutting Gotchas

- opencode.json previously had `"edit": "deny"` — agent can now edit files.
- Backend `KeyClock` section name typo is intentional — do NOT fix it.
- Only `DatabaseProvider=SqlServer` works; MySql is stubbed to throw.
- `FE/src/environments/environment.ts` targets https://localhost:7002/api.
- `DockerImages/docker-compose.yml` has plaintext dev credentials — keep out of logs.
- No CI workflows exist yet (`.github/workflows/` is empty).
- Two EF migration files are gitignored (InitialCreate).
- **OpenCode server auto-starts** with the backend via `OpenCodeServerHostedService` (port 3000, Big Pickle LLM). No manual `opencode serve` needed.
- **Big Pickle LLM** (`opencode/big-pickle`) is the default AI model. Configured in `appsettings.json` → `OpencodeAi`.
- **MCP filesystem** is configured in `opencode.json` — gives Big Pickle access to the full workspace.

## 🐢 Speed Rules — read before touching a running service

This project is slow to verify, and the slowness is almost always self-inflicted by
rebuilding/restarting more than necessary. These rules exist because each one was
learned the hard way. **Follow them and you will not need to ask the user to hurry.**

### 1. Never build while the backend is running

A running `AutoApiEngine.ApiServices` locks its own output DLLs, so `dotnet build`
fails with `MSB3027` / `MSB3021` ("file is locked by …"). You will then burn a
`--nologo -v q` build, hit the same error, and go around again.

**Stop → build → start in ONE tool call, and do it once per batch of edits:**

```powershell
# single call: stop on 5145/7002, build, restart, wait for the port
Get-NetTCPConnection -State Listen -LocalPort 5145 -ErrorAction SilentlyContinue |
  ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
  Where-Object { $_.CommandLine -like '*ApiServices*' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Start-Sleep 3
dotnet build "BE\AutoApiEngine.ApiServices\AutoApiEngine.ApiServices.csproj" -v q --nologo
# only restart if the build succeeded
```

**Batch your edits, then build once.** Do not build after every single file edit.
`dotnet build` on this solution takes ~30-60s; three builds for three files is pure waste.

### 2. Batch every runtime probe into one call

All HTTP checks, `docker exec mysql`, and log reads belong in **one** PowerShell call.
One call per probe is the single biggest time sink. Mint the token once, store it in a
variable, and reuse it for every request in that same call.

### 3. Keep services running between steps

Leave the backend and frontend up while you work. Only stop them when the user asks,
and if you do, **say what the consequence is** — a stopped frontend means "unable to
login", which is a confusing symptom rather than an obvious one.

Never stop the backend just to run a build. Use `dotnet build` against a separate
output path, or do the stop/build/start dance in one shot per §1.

### 4. Getting a JWT for scripted API calls (took ~6 failed attempts to work out)

`POST /api/auth/login` is an OAuth2 **code exchange** and cannot be scripted. Going
straight to Keycloak's token endpoint also fails, and the error is misleading:

- The **live** `api-engine-app` client (the checked-in `realm-config.json` does not
  carry this) is `publicClient: false` **and** `clientAuthenticatorType: "client-jwt"`.
- A password grant with the **correct** `client_secret` still returns
  `invalid_client: "Parameter client_assertion_type is missing"` — the client will not
  accept secret auth at all.
- The realm's only user is `fadi.freij@hotmail.com`; its password is not in the repo.

**What works:** sign the same private-key JWT that `KeycloakService.AddClientAssertion`
builds. The private key is in `appsettings.json` → `KeyClock:ClientJwtKey` (JWK
components `N,E,D,P,Q,DP,DQ,QI,Kid`). Sign RS256 over
`{iss, sub, aud:"http://localhost:8081/realms/ApiEngineRealm/protocol/openid-connect/token", jti, iat, exp}`
and send it as `client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer`
+ `client_assertion`. Keep `exp` ~60s ahead. The result is a normal user token with the
`organization` claim. Tokens expire in **300s** — mint a fresh one per verification batch.

If no verification user exists, create a temporary realm user, put it in the
**Test Organization** group (that is where the `organization` claim comes from), and
**delete it when finished**.

### 5. Read the 2-3 files instead of spawning a sub-agent

A sub-agent round-trip costs a full model turn plus its own file reads. If you already
know *which* service or type to look at, just `grep`/`read` it. Delegate only when the
search space is genuinely unknown or the edit is large.

### 6. Verify endpoint shapes before writing assertions

Two wrong guesses cost full round-trips in this session:
- `GET /api/schema/{id}/columns` takes **`?table=`**, not `?objectName=`.
- `POST /api/keys` returns the plaintext as **`plainKey`**, not `apiKey` (64 chars,
  shown once; the DB stores only its SHA-256 hex hash).

Check the controller signature or DTO before building a request or parsing a response.

### 7. Starting the frontend in the background

`Start-Process npm` fails with *"%1 is not a valid Win32 application"* — `npm` is a
shim. Use:

```powershell
Start-Process cmd.exe -ArgumentList "/c","npm start" -WorkingDirectory "D:\AutoCrud_Full\FE" `
  -WindowStyle Hidden -RedirectStandardOutput "<temp>\fe.log" -RedirectStandardError "<temp>\fe.err"
```

Port 4200 is typically listening ~15s later. `FE/proxy.conf.json` forwards `/api` and
`/hubs` to **https://localhost:7002**, so the **https** backend must be up for login to
work — `environment.ts` uses a relative `apiUrl: '/api'`.

### 8. Ask before widening scope, then verify the fix actually runs

When a fix requires a change outside the plan, present the options and get a decision
in one shot. After implementing, prove it end-to-end (here: a cross-check of the new
field against the endpoint that already enforced it) rather than declaring it done
because it compiles.

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
