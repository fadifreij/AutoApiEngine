# Frontend Reference (FE/)

## Runtime

- Angular 21, standalone components (no NgModules)
- Build: `@angular/build` (Vite-based), output mode: `server` (SSR)
- SSR: All routes prerendered via `RenderMode.Prerender`
- Test: `vitest` (via `ng test`)
- Styles: SCSS (component-scoped + global `src/styles.scss`)
- Package manager: npm 10.9.7

---

## Component Tree

```
App (app.ts)
├── app-loading (LoadingComponent)
└── <router-outlet>
    ├── Landing          [/]              public
    ├── Login            [/login]         public -> redirects to Keycloak
    ├── Register         [/register]      public
    ├── AuthCallback     [/auth/callback]  handles OAuth code exchange
    ├── Pricing          [/pricing]
    ├── Docs             [/docs]
    └── DashboardLayout  [/app]           protected
        ├── Dashboard           [/app/dashboard]
        ├── WorkspaceNew        [/app/workspace/new]
        ├── WorkspaceManage     [/app/workspace/manage]
        ├── ApiGenerated        [/app/apis/generated]
        └── ApiCustom           [/app/apis/custom]

    QueryStudio         [/query-studio]   protected (outside app layout)
```

## Routes (from app.routes.ts)

| Path | Guard | Component |
|------|-------|-----------|
| `` | `publicGuard` | Landing |
| `login` | `publicGuard` | Login |
| `register` | `publicGuard` | Register |
| `auth/callback` | none | AuthCallback |
| `pricing` | none | Pricing |
| `docs` | none | Docs |
| `app` | `authGuard` | DashboardLayout (shell) |
| `app/dashboard` | inherited | Dashboard |
| `app/workspace/new` | inherited | WorkspaceNew |
| `app/workspace/manage` | inherited | WorkspaceManage |
| `app/apis/generated` | inherited | ApiGenerated |
| `app/apis/custom` | inherited | ApiCustom |
| `app` (empty) | inherited | redirects to `dashboard` |
| `query-studio` | `authGuard` | QueryStudio |

---

## Guards

### authGuard
- Server: returns true
- Browser: if `isAuthenticated()`, pass; else `initAuth()` then redirect to `/login` on failure

### publicGuard
- Server: returns true
- Browser: if `isAuthenticated()`, redirect to `/app/dashboard`; else `initAuth()` -> redirect if session restored, else pass

---

## Services

### AuthService (`shared/auth/auth.service.ts`)
- Signals: `accessToken`, `currentUser`, `isAuthenticated`
- API calls: `/auth/Login`, `/auth/register`, `/auth/logout`, `/auth/refresh-token`
- Cross-tab sync via `window 'storage'` event on `id_token`

### LoadingService (`shared/loading/loading.service.ts`)
- Signal: `visible`
- Methods: `show()`, `hide()`
- Consumed by root `App` component

---

## Shared Components

### LoadingComponent (`shared/loading/loading.component.ts`)
- Selector: `app-loading`
- Shows overlay with CSS spinner when `LoadingService.visible()` is true
- Used in root `app.html`

---

## Conventions

- Co-located files: `feature/feature.ts|.html|.scss`
- `inject()` preferred over constructor injection (except AuthService which uses constructor + PLATFORM_ID)
- Template-driven forms only (no ReactiveFormsModule imports found)
- No HTTP interceptors - components call HttpClient directly
- JSON serialization: camelCase + enum strings (BE matches this)

---

## Environment

```typescript
// development
{ apiUrl: 'https://localhost:7002/api', keycloakUrl: 'http://localhost:8081', clientId: 'api-engine-app' }

// production
{ apiUrl: '', keycloakUrl: '', clientId: '' }  // injected at deploy time
```

---

## Current Incomplete Areas

- **WorkspaceNew component** - has signals (`dbType`, `showEncryptionKey`) but no `ngModel` bindings, no HTTP service, no form submission
- **WorkspaceManage component** - mock progress popups with `setInterval`, no real HTTP/SignalR
- **No models/interfaces** for workspace DTOs on frontend (only `RegisterRequest` and `AuthResponse` exist)
- **No HTTP interceptors** - every component must handle `withCredentials` for cookie-based refresh
