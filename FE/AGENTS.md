# AutoApiEngine Frontend (FE/)

Run all npm commands from `FE/`. The repo root has no `package.json`.

## Commands

| Command | Purpose |
|---------|---------|
| `npm install` | Install deps (lockfile: `package-lock.json`, manager: `npm@10.9.7`) |
| `npm start` | Dev server @ http://localhost:4200 |
| `npm test` | Unit tests (Vitest via `ng test`) |
| `npm run build` | SSR build (`outputMode: "server"`) |
| `npm run serve:ssr:my-project` | Serve SSR from `dist/my-project/server/server.mjs` |

## Architecture

- **Standalone components**, lazy-loaded via `loadComponent()` in `app.routes.ts`.
- **Server prerender**: `app.routes.server.ts` with `RenderMode.Prerender` at `path: '**'`.
- **SSR-safe guards**: early-return via `isPlatformBrowser(inject(PLATFORM_ID))`.

## Auth / HTTP

- `apiUrl`: `https://localhost:7002/api`, `keycloakUrl`: `http://localhost:8081`, `clientId`: `api-engine-app` (see `src/environments/environment.ts`).
- Cookie-based refresh: use `withCredentials: true` (see `shared/auth/auth.service.ts`).
- No HTTP interceptors — direct `HttpClient` calls.

## ⚠️ "Unable to login" troubleshooting — check this first

| Symptom | Cause | Fix |
|---|---|---|
| Login page unreachable | **FE dev server not running** (port 4200) | `Start-Process cmd.exe -ArgumentList "/c","npm start" -WorkingDirectory "D:\AutoCrud_Full\FE" -WindowStyle Hidden` — plain `Start-Process npm` fails, `npm` is a `.cmd` shim |
| Login submits but 401s | **Backend not running** (5145/7002) | `dotnet run --project ... --launch-profile https` |
| Proxy errors / `ECONNREFUSED` in console | Backend not up — `proxy.conf.json` forwards `/api` + `/hubs` to **https://localhost:7002**, and `environment.ts` uses a relative `apiUrl: '/api'`, so the **https** backend is mandatory | Start the https profile, not the http one |
| Keycloak errors in console | Keycloak container down (8081) | `docker compose up -d` in `DockerImages/` |

`ng serve` needs ~15s before 4200 is listening — wait for the port rather than
assuming it failed.

**Do not stop the FE as a cleanup step.** A stopped frontend presents as "unable to
login", which reads like an auth bug and sends the investigation in the wrong
direction. Leave it running between steps.

## Conventions

- Co-location: `feature/feature.ts|.html|.scss`.
- Prefer `inject()`; `AuthService` uses constructor injection + `PLATFORM_ID`.
- Template-driven forms (no `ReactiveFormsModule` usage found).

## Detailed Reference

See `.opencode/agents/frontend.md` for the full sub-agent instructions.
