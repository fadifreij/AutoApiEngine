# AutoApiEngine Frontend - Development Guide

## Tech Stack
- **Angular 21.2.0** - Fully standalone components, zero NgModules
- **Signals** for all reactive state (no BehaviorSubject/Subject in components)
- **Template-Driven Forms** only (no ReactiveFormsModule)
- **SCSS** with CSS custom properties (design tokens)
- **SSR** with prerendering (all routes use RenderMode.Prerender)

## Project Structure

```
src/
  app/
    app.config.ts            - Application config (providers)
    app.config.server.ts     - Server-side config
    app.routes.ts            - Route definitions
    app.routes.server.ts     - Server route config (prerender)
    app.ts                   - Root component
    app.html                 - Root template (router-outlet + loading)
    app.scss                 - Root styles (empty)
    layouts/
      dashboard-layout/      - Authenticated shell layout
    pages/                   - Public pages
      landing/
      login/
      register/
      auth-callback/
      pricing/
      docs/
    dashboard/               - Authenticated pages
      dashboard/
      workspace-new/
      workspace-manage/
      api-generated/
      api-custom/
      query-studio/
    shared/
      auth/
        auth.service.ts      - Auth state and operations
        auth.guard.ts        - Route guards
      loading/
        loading.component.ts - Global loading spinner
        loading.service.ts   - Loading state management
  environments/
    environment.ts           - Dev environment config
    environment.prod.ts      - Prod environment config
  styles.scss                - Global design tokens and utilities
  index.html
  main.ts                    - Browser bootstrap
  main.server.ts             - Server bootstrap
  server.ts                  - Express server
```

## Component Pattern

Every component uses the co-located file pattern:
```
component-name/
  component-name.ts
  component-name.html
  component-name.scss
```

### TypeScript Component Template
```typescript
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-component-name',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './component-name.html',
  styleUrl: './component-name.scss'
})
export class ComponentName {
  // Use inject() for DI, not constructor injection
  private router = inject(Router);

  // State with signals
  isOpen = signal(false);

  // Methods
  toggle() {
    this.isOpen.set(!this.isOpen());
  }
}
```

## DI Pattern
- Use **inject()** function for DI in components (not constructor injection)
- Exception: AuthService uses constructor + @Inject(PLATFORM_ID) for SSR safety
- Services are providedIn: 'root' singletons

## Signal Usage
- All component state uses signal()
- Use computed() for derived state
- No BehaviorSubject, no RxJS Subject in components
- Pattern: stateName = signal(initialValue)
- Read: stateName() | Write: stateName.set(newValue)

## Routing Pattern

### Route definitions (app.routes.ts)
- All routes use **loadComponent** for lazy loading (code splitting)
- No loadChildren with NgModules
- Route guards: authGuard, publicGuard
- Parent route /app uses DashboardLayout as wrapper
- QueryStudio is a sibling route to /app (not nested)

### Route structure
```
/                         -> Landing (publicGuard)
/login                    -> Login (publicGuard)
/register                 -> Register (publicGuard)
/auth/callback            -> AuthCallback (no guard)
/pricing                  -> Pricing (no guard)
/docs                     -> Docs (no guard)
/app                      -> DashboardLayout (authGuard)
  /app/dashboard          -> Dashboard
  /app/workspace/new      -> WorkspaceNew
  /app/workspace/manage   -> WorkspaceManage
  /app/apis/generated     -> ApiGenerated
  /app/apis/custom        -> ApiCustom
/app/query-studio         -> QueryStudio (authGuard, sibling to /app)
```

## Guards Pattern

### authGuard
- Blocks unauthenticated users
- First checks auth.isAuthenticated() synchronously
- If false, calls auth.initAuth() (refresh token flow)
- Navigates to /login if still unauthenticated
- Returns true during SSR (isPlatformBrowser check)

### publicGuard
- Blocks authenticated users from public pages
- Redirects to /app/dashboard if already logged in
- Tries initAuth() to restore session
- Returns true during SSR

## Services Pattern

### AuthService
- providedIn: 'root' singleton
- Uses signals for state: accessToken, currentUser, isAuthenticated
- Stores idToken in sessionStorage (browser-only check)
- OAuth2 Authorization Code Flow with Keycloak
- Methods: login(), handleCallback(code), register(data), logout(), refreshToken(), getAccessToken(), getOrganization()

### LoadingService
- providedIn: 'root' singleton
- Single signal: visible = signal(false)
- Methods: show(), hide()

## HTTP Pattern
- HttpClient configured with provideHttpClient(withFetch())
- No HTTP interceptors
- Auth requests use { withCredentials: true } for cookie-based refresh tokens
- API base URL from environment.apiUrl
- No shared API service layer - components call HttpClient directly

## Form Handling
- **Template-Driven Forms** only (FormsModule, not ReactiveFormsModule)
- [(ngModel)] bindings with name attribute on inputs
- Manual validation in component code
- Some UI uses pure signal state (no form submission)

## SCSS Architecture

### Global styles (styles.scss)
Design tokens on :root:
- Colors: --primary (#4f46e5), --primary-dark, --primary-light, --primary-bg, --accent (#7c3aed)
- States: --success, --warning, --danger
- Grays: --gray-50 through --gray-900
- Fonts: --font (Segoe UI), --mono (Consolas)
- Radius: --radius (10px), --radius-lg (16px)
- Shadows: --shadow-sm, --shadow-md, --shadow-lg

### Component styles
- Co-located .scss file per component
- Uses :host selector for component-level layout
- No mixins, no functions, no partials
- Nested selectors with & syntax
- References design tokens via var(--...)
- Heavy use of inline SVGs (no icon library)

### Global button classes
.btn, .btn-primary, .btn-outline, .btn-ghost, .btn-lg, .btn-sm, .btn-white, .btn-danger, .btn-success

## Auth Flow

1. **Login**: Component calls AuthService.login() -> redirects to Keycloak URL
2. **Keycloak**: User authenticates on branded Keycloak login page (apiengine theme)
3. **Callback**: Keycloak redirects to /auth/callback?code=... -> AuthCallback extracts code -> POSTs to backend /api/auth/Login -> stores accessToken in signal, idToken in sessionStorage -> redirects to /app/dashboard
4. **Session Restoration**: authGuard calls auth.initAuth() -> POSTs to /api/auth/refresh-token with credentials -> updates accessToken signal
5. **Logout**: Clears signals, removes idToken from sessionStorage, POSTs to /api/auth/logout -> redirects to Keycloak end-session URL

### Token Storage
- Access token: memory only (Angular signal)
- ID token: sessionStorage (for logout flow)
- Refresh token: HTTP-only cookie (backend-managed)

## Environment Config
- environment.ts (dev): apiUrl: https://localhost:7002/api, keycloakUrl: http://localhost:8081, clientId: api-engine-app
- environment.prod.ts (prod): placeholder values (to be filled during deployment)
- File replacement configured in angular.json

## SSR Configuration
- outputMode: "server" in angular.json
- provideClientHydration(withEventReplay()) in app.config.ts
- All routes use RenderMode.Prerender in app.routes.server.ts
- SSR-aware guards check isPlatformBrowser(PLATFORM_ID)
- AuthCallback skips execution during SSR

## Key URLs and Ports
- Angular dev server: http://localhost:4200
- Angular dev server (HTTPS): https://localhost:7002
- Backend API: https://localhost:7002/api
- Keycloak: http://localhost:8081

## Important Notes
- No state management library (no NgRx, NGXS, Akita)
- No HTTP interceptors
- No icon library (inline SVGs throughout)
- No shared component library (all components are page-specific)
- No ReactiveFormsModule used anywhere
- No OnPush change detection explicitly set
- SSR must be considered: always guard browser-only APIs with isPlatformBrowser
- Keycloak scope includes "organization" for org claim in tokens
