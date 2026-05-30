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

## Conventions

- Co-location: `feature/feature.ts|.html|.scss`.
- Prefer `inject()`; `AuthService` uses constructor injection + `PLATFORM_ID`.
- Template-driven forms (no `ReactiveFormsModule` usage found).

## Detailed Reference

See `.opencode/agents/frontend.md` for the full sub-agent instructions.
