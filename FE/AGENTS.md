# AutoApiEngine Frontend (FE/)

Run frontend commands from `FE/` (repo root has no `package.json`).

## Commands
- Install: `npm install` (lockfile is `FE/package-lock.json`; `packageManager` is `npm@10.9.7`).
- Dev: `npm start` (`ng serve`, default `http://localhost:4200`).
- Unit tests: `npm test` (`ng test`).
- Build (SSR output): `npm run build` (`ng build`, `outputMode: "server"` in `angular.json`).
- Serve built SSR output: `npm run serve:ssr:my-project` (runs `dist/my-project/server/server.mjs`).

## Routing / SSR
- Browser routes live in `src/app/app.routes.ts` and are lazy-loaded via `loadComponent()` (no NgModules in-tree).
- Server prerender is configured in `src/app/app.routes.server.ts` as `path: '**'` with `RenderMode.Prerender` (everything prerenders unless you change it).
- Guards must be SSR-safe: existing guards early-return on server via `isPlatformBrowser(inject(PLATFORM_ID))`.

## Auth / HTTP
- Default env config is in `src/environments/environment.ts`: `apiUrl` is `https://localhost:7002/api`, `keycloakUrl` is `http://localhost:8081`, `clientId` is `api-engine-app`.
- Refresh-token flow is cookie-based; calls that rely on it must pass `withCredentials: true` (see `shared/auth/auth.service.ts`).
- No HTTP interceptors are wired; components/services call `HttpClient` directly.

## Conventions Used In This App
- Component co-location: `feature/feature.ts|.html|.scss`.
- Prefer `inject()` in components; `AuthService` uses constructor injection + `PLATFORM_ID` because it touches `localStorage`.
- Forms are template-driven (there is no `ReactiveFormsModule` usage in `src/`).
