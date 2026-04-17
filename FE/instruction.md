# Auto API Engine - Angular 21 SSR Project

## Project Status: ✅ FULLY UPGRADED & OPERATIONAL

This Angular SSR (Server-Side Rendering) project has been fully upgraded to **Angular 21.2.8** with all dependencies resolved and best practices applied.

---

## 📋 Changes Made

### 1. TypeScript Configuration
- ✅ Fixed `tsconfig.app.json` by adding missing `rootDir: "./src"` configuration

### 2. Dependencies Updated
- ✅ Added `@angular/animations@^21.2.0` (required for async animations)
- ✅ Added `primeng@^21.0.0` (compatible with Angular 21)
- All Angular packages upgraded to compatible 21.x versions

### 3. Components & Routing
- ✅ Created missing shell layout component
- ✅ Created missing 404 not-found component
- ✅ Generated complete feature modules:
  - Dashboard (authenticated user dashboard)
  - Workspace (workspace management)
  - Admin (admin panel)
- ✅ All components use standalone architecture (no NgModules)

### 4. Architecture Improvements
- ✅ Consolidated root component (removed duplicate)
- ✅ Refactored PrimeNG theme switcher for compatibility
- ✅ Created theme asset files (dark, light, ocean, forest, midnight)
- ✅ Updated test specifications for modern patterns

### 5. SSR Configuration
- ✅ Disabled prerendering (not applicable for protected routes)
- ✅ Verified Express server configuration
- ✅ Confirmed Angular Universal setup

---

## 🚀 How to Run the Project

### Prerequisites
- Node.js 22.x or higher
- npm 10.x or higher
- Angular CLI 21.2.x (installed globally or via npx)

### Step 1: Install Dependencies
```bash
cd c:\MyFolder\ApiEngine\autoApiEngine
npm install
```

### Step 2: Verify Installation
```bash
ng version
```

Expected output should show:
- Angular CLI: 21.2.7+
- Angular: 21.2.8+
- Node.js: 22.x+

### Step 3: Run Development Server
```bash
npm start
# or
ng serve
```

**Output:**
```
✔ Compiled successfully
Local:   http://localhost:4200/
```

Open browser to `http://localhost:4200/` (automatically opens in dev mode)

### Step 4: Build for Production
```bash
npm run build
```

**Output:**
- Browser bundles in `dist/autoApiEngine/browser/`
- Server bundles in `dist/autoApiEngine/server/`

### Step 5: Run SSR Server (Production)
```bash
npm run serve:ssr:autoApiEngine
```

**Output:**
```
Node Express server listening on http://localhost:4000
```

Visit `http://localhost:4000` to see server-rendered app

### Step 6: Run Tests
```bash
npm test
```

Launches test runner (Vitest) with all project tests

---

## 📁 Project Structure

```
src/
├── app/
│   ├── app.ts                          # Root component
│   ├── app.config.ts                   # App configuration
│   ├── app.routes.ts                   # Route definitions
│   ├── app.spec.ts                     # App tests
│   │
│   ├── auth/                           # Authentication feature
│   │   ├── pages/signin                # Sign-in component
│   │   ├── pages/signup                # Sign-up component
│   │   ├── services/auth.service.ts    # Auth API service
│   │   └── store/auth.store.ts         # Auth state
│   │
│   ├── dashboard/                      # Dashboard feature
│   ├── workspace/                      # Workspace management
│   ├── admin/                          # Admin panel
│   │
│   ├── shared/                         # Shared components
│   │   └── components/
│   │       ├── shell/                  # Layout wrapper
│   │       └── not-found/              # 404 page
│   │
│   └── core/                           # Core services
│       ├── guards/                     # Route guards (auth, guest, role)
│       ├── interceptors/               # HTTP interceptors
│       ├── services/                   # Notification, Theme services
│       └── theme/                      # Theme management
│
├── main.ts                             # Browser bootstrap
├── main.server.ts                      # Server bootstrap
├── server.ts                           # Express server
└── styles.scss                         # Global styles
```

---

## 🔐 Authentication Flow

The app implements role-based authentication:

1. **Guest Routes** (`/auth/signin`, `/auth/signup`)
   - Redirects authenticated users to dashboard
   - Protected by `guestGuard`

2. **Protected Routes** (`/dashboard`, `/workspace`, `/admin`)
   - Requires authentication
   - Protected by `authGuard`
   - Admin section protected by `roleGuard`

3. **Auth Store**
   - Manages user, org, token state
   - Persists to localStorage
   - Calculated signals: `isAuthenticated`, `isAdmin`, `isDeveloper`

4. **JWT Interceptor**
   - Automatically adds bearer token to requests
   - Handles token expiration (401 errors)

---

## 🎨 Theming System

Five themes available (Dark, Light, Ocean, Forest, Midnight):

- Theme CSS files: `public/assets/themes/{name}/theme.css`
- Runtime switching via theme-switcher component
- CSS Custom Properties (variables) for customization
- Persistence to localStorage

**Switch themes:**
- Click theme icon in top toolbar
- Select desired theme from dropdown
- Changes persist across sessions

---

## 📦 Key Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| @angular/core | ^21.2.0 | Angular framework |
| @angular/ssr | ^21.2.5 | Server-side rendering |
| primeng | ^21.0.0 | UI component library |
| express | ^5.1.0 | Node.js server |
| rxjs | ~7.8.0 | Reactive programming |
| typescript | ~5.9.2 | TypeScript compiler |

---

## 🛠️ Development Commands

| Command | Description |
|---------|-------------|
| `npm start` | Start dev server (http://localhost:4200) |
| `npm run build` | Production build (SSR + browser bundles) |
| `npm test` | Run unit tests (Vitest) |
| `npm run watch` | Watch mode for development |
| `npm run serve:ssr:autoApiEngine` | Run SSR server (http://localhost:4000) |

---

## 💾 TypeScript Configuration

- **Target**: ES2022
- **Module**: preserve (ESM)
- **Strict Mode**: Enabled
- **Configuration**: `tsconfig.json` + `tsconfig.app.json`

---

## ⚠️ Known Limitations

1. **Prerendering**: Disabled (protected routes prevent static prerendering)
2. **localStorage**: Only available in browser (auth service handles SSR gracefully)
3. **Themes**: CSS files must exist in public/assets/themes/ for theming to work

---

## 🔍 Troubleshooting

### Dev server won't start
```bash
# Clear node_modules and .angular cache
rm -r node_modules .angular
npm install
npm start
```

### Build fails
```bash
# Verify TypeScript compilation
ng build --version
# Check for type errors
ng analyze
```

### SSR server errors
```bash
# Ensure dist/ is built
npm run build
# Then run server
npm run serve:ssr:autoApiEngine
```

### Port already in use
```bash
# Dev server (change port)
ng serve --port 4201

# SSR server (change PORT env var)
PORT=4001 npm run serve:ssr:autoApiEngine
```

---

## 📚 Modern Angular Patterns

This project exemplifies current Angular best practices:

- ✅ **Standalone Components** - No NgModules required
- ✅ **Functional Routing** - Route guards as functions
- ✅ **Signals API** - Reactive state management
- ✅ **Typed Forms** - Fully typed form controls
- ✅ **HttpInterceptors** - Functional interceptor pattern
- ✅ **Async Animations** - Keep bundle size small
- ✅ **OnPush Detection** - Optimal change detection
- ✅ **Control Flow** - @if, @for, @switch syntax
- ✅ **Lazy Loading** - Feature routes loaded on demand
- ✅ **SSR Ready** - Full server-side rendering support

---

## 📝 Notes for Developers

1. **New Components**: Use `ng generate component path/name` for consistent scaffolding
2. **Feature Modules**: Place under `src/app/{feature}/` with routes file
3. **Services**: Use `providedIn: 'root'` and `inject()` pattern
4. **State Management**: Use signals and computed for reactive state
5. **Styles**: Use SCSS (configured in angular.json)
6. **Testing**: Use Vitest framework (jsdOM configured)

---

## ✨ Next Steps

It's recommended to:
1. Customize theme colors in `public/assets/themes/`
2. Implement actual auth API endpoints
3. Add feature modules as needed
4. Configure API base URL for backend
5. Set up CI/CD pipeline for automated deployments

---

**Project Status**: ✅ Ready for Development & Production  
**Last Updated**: April 9, 2026  
**Angular Version**: 21.2.8


If needed, ask clarifying questions before making changes.
