# Home Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a new `/home` post-login landing page with three sections: Workspaces, API Management, and Security, plus update routing and shell navigation.

**Architecture:** Single `HomeComponent` with three inline sections (Workspaces, API Management, Security) as a scrollable page. Each section uses `--aae-*` theme CSS variables for multi-theme support. Navigation via Angular `RouterLink`. Workspace data sourced from existing `WorkspaceStore`.

**Tech Stack:** Angular 19 (standalone components, signals, OnPush), SCSS with CSS custom properties, PrimeNG Button/Card modules, Angular Router.

---

## File Map

### New files:
- `FE/src/app/home/home.routes.ts` — route config for `/home`
- `FE/src/app/home/home.component.ts` — standalone component with `@Component` decorator
- `FE/src/app/home/home.component.html` — template with three sections
- `FE/src/app/home/home.component.scss` — styles using `--aae-*` variables

### Modified files:
- `FE/src/app/auth/constants.ts:2` — change `url_after_login` from `'/workspaces'` to `'/home'`
- `FE/src/app/app.routes.ts` — add `/home` route, keep root redirect → `/home`
- `FE/src/app/shared/components/shell/shell.component.ts` — update `shell__topbar-nav` template with Home/API/Security links

---

## Task 1: Update `url_after_login` constant

**Files:**
- Modify: `FE/src/app/auth/constants.ts:2`

- [ ] **Step 1: Update constant**

```typescript
export const url_after_login = '/home'
```

---

## Task 2: Create home routes file

**Files:**
- Create: `FE/src/app/home/home.routes.ts`

- [ ] **Step 1: Create home routes**

```typescript
import { Routes } from '@angular/router';

export const homeRoutes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./home.component').then((m) => m.HomeComponent),
    title: 'Dashboard — Auto API Engine',
  },
];
```

---

## Task 3: Update app routes

**Files:**
- Modify: `FE/src/app/app.routes.ts:9` — change root redirect from `'workspaces'` to `'home'`
- Modify: `FE/src/app/app.routes.ts` — add home routes to protected shell children

- [ ] **Step 1: Update root redirect**

Change:
```typescript
{ path: '', redirectTo: 'workspaces', pathMatch: 'full' },
```
To:
```typescript
{ path: '', redirectTo: 'home', pathMatch: 'full' },
```

- [ ] **Step 2: Add home to shell children**

Add inside the protected shell `children` array (before the `admin` entry):
```typescript
{
  path: 'home',
  loadChildren: () =>
    import('./home/home.routes').then((m) => m.homeRoutes),
},
```

---

## Task 4: Update shell topbar navigation

**Files:**
- Modify: `FE/src/app/shared/components/shell/shell.component.ts` — update `shell__topbar-nav` template

- [ ] **Step 1: Replace nav links**

Replace the `shell__topbar-nav` nav section with:
```typescript
<nav class="shell__topbar-nav">
  <a routerLink="/home" routerLinkActive="active" class="shell__nav-link">
    <i class="pi pi-home"></i>
    <span>Home</span>
  </a>
  <a routerLink="/workspaces" routerLinkActive="active" class="shell__nav-link">
    <i class="pi pi-database"></i>
    <span>Workspaces</span>
  </a>
  <a routerLink="/api-management" routerLinkActive="active" class="shell__nav-link">
    <i class="pi pi-bolt"></i>
    <span>API Management</span>
  </a>
  <a routerLink="/security" routerLinkActive="active" class="shell__nav-link">
    <i class="pi pi-shield"></i>
    <span>Security</span>
  </a>
</nav>
```

---

## Task 5: Create home component TypeScript

**Files:**
- Create: `FE/src/app/home/home.component.ts`

- [ ] **Step 1: Create HomeComponent**

```typescript
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { WorkspaceStore } from '../workspace/store/workspace.store';

@Component({
  selector: 'app-home',
  imports: [RouterLink, ButtonModule, CardModule],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomeComponent {
  protected readonly store = inject(WorkspaceStore);
  private readonly router = inject(Router);

  protected readonly activeWorkspaceCount = computed(
    () => this.store.workspaces().filter((w) => w.isActive).length
  );

  protected readonly totalEndpoints = computed(() =>
    this.store.workspaces().reduce((acc, ws) => {
      const tableCount = ws.children
        .find((c) => c.label === 'Tables')
        ?.children?.length ?? 0;
      return acc + tableCount * 4; // 4 endpoints per table (GET/POST/PUT/DELETE)
    }, 0)
  );

  navigateToCreate(): void {
    this.router.navigate(['/workspaces/create']);
  }

  formatSize(bytes?: number): string {
    if (!bytes) return '—';
    const mb = bytes / (1024 * 1024);
    return mb >= 1024 ? `${(mb / 1024).toFixed(1)} GB` : `${mb.toFixed(0)} MB`;
  }

  formatDate(date?: Date): string {
    if (!date) return 'Never';
    return date.toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
  }
}
```

---

## Task 6: Create home component HTML template

**Files:**
- Create: `FE/src/app/home/home.component.html`

- [ ] **Step 1: Create full HTML template**

```html
<div class="home">

  <!-- ── Hero ───────────────────────────────────────────────────── -->
  <header class="home__hero">
    <div class="home__hero-text">
      <h1>Welcome back</h1>
      <p>Manage your databases, APIs, and security all in one place</p>
    </div>
    <div class="home__hero-actions">
      <a routerLink="/workspaces/create" class="btn btn--primary">
        <svg viewBox="0 0 20 20" fill="currentColor" width="18" height="18">
          <path fill-rule="evenodd" d="M10 3a1 1 0 011 1v5h5a1 1 0 110 2h-5v5a1 1 0 11-2 0v-5H4a1 1 0 110-2h5V4a1 1 0 011-1z" clip-rule="evenodd" />
        </svg>
        New Workspace
      </a>
    </div>
  </header>

  <!-- ── Section 1: Workspaces ────────────────────────────────── -->
  <section class="home__section">
    <div class="section-header">
      <div class="section-header__icon section-header__icon--green">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="24" height="24">
          <ellipse cx="12" cy="5" rx="9" ry="3" />
          <path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3" />
          <path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5" />
        </svg>
      </div>
      <div class="section-header__text">
        <h2>Workspaces</h2>
        <p>Your database workspaces — create, manage, backup, and restore</p>
      </div>
    </div>

    <div class="stats-strip">
      <div class="stat-pill">
        <span class="stat-pill__value">{{ store.workspaceCount() }}</span>
        <span class="stat-pill__label">Workspaces</span>
      </div>
      <div class="stat-pill">
        <span class="stat-pill__value">{{ activeWorkspaceCount() }}</span>
        <span class="stat-pill__label">Active</span>
      </div>
    </div>

    <div class="ws-grid">
      @for (ws of store.workspaces(); track ws.id) {
        <div class="ws-card">
          <div class="ws-card__header">
            <svg class="ws-card__db-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="22" height="22">
              <ellipse cx="12" cy="5" rx="9" ry="3" />
              <path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3" />
              <path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5" />
            </svg>
            <span class="ws-card__name">{{ ws.name }}</span>
            <span class="ws-badge" [class]="'ws-badge--' + ws.status">{{ ws.status }}</span>
          </div>
          <div class="ws-card__meta">
            <span>{{ ws.dbType | uppercase }}</span>
            <span>{{ formatSize(ws.sizeBytes) }}</span>
            <span>Backup: {{ formatDate(ws.lastBackupDate) }}</span>
          </div>
          <div class="ws-card__actions">
            <a routerLink="/workspaces/dashboard" class="btn btn--sm btn--outline">Manage</a>
          </div>
        </div>
      } @empty {
        <div class="ws-empty">
          <p>No workspaces yet. Create your first one to get started.</p>
          <a routerLink="/workspaces/create" class="btn btn--primary">Create Workspace</a>
        </div>
      }
    </div>

    <div class="section-footer">
      <a routerLink="/workspaces/dashboard" class="btn btn--secondary">
        All Workspaces
        <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16">
          <path fill-rule="evenodd" d="M7.293 14.707a1 1 0 010-1.414L10.586 10 7.293 6.707a1 1 0 011.414-1.414l4 4a1 1 0 010 1.414l-4 4a1 1 0 01-1.414 0z" clip-rule="evenodd" />
        </svg>
      </a>
    </div>
  </section>

  <!-- ── Section 2: API Management ─────────────────────────────── -->
  <section class="home__section">
    <div class="section-header">
      <div class="section-header__icon section-header__icon--blue">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="24" height="24">
          <path d="M13 10V3L4 14h7v7l9-11h-7z" />
        </svg>
      </div>
      <div class="section-header__text">
        <h2>API Management</h2>
        <p>Generate REST APIs for each table in your database workspaces</p>
      </div>
    </div>

    <div class="stats-strip">
      <div class="stat-pill">
        <span class="stat-pill__value">{{ store.workspaceCount() }}</span>
        <span class="stat-pill__label">APIs Generated</span>
      </div>
      <div class="stat-pill">
        <span class="stat-pill__value">{{ totalEndpoints() }}</span>
        <span class="stat-pill__label">Endpoints</span>
      </div>
    </div>

    <div class="api-ws-list">
      @for (ws of store.workspaces(); track ws.id) {
        @let tableCount = ws.children.find(c => c.label === 'Tables')?.children?.length ?? 0;
        <div class="api-ws-item">
          <div class="api-ws-item__info">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="18" height="18">
              <ellipse cx="12" cy="5" rx="9" ry="3" />
              <path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3" />
              <path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5" />
            </svg>
            <span class="api-ws-item__name">{{ ws.name }}</span>
            <span class="api-ws-item__tables">{{ tableCount }} tables</span>
          </div>
          <div class="api-ws-item__status">
            <span class="status-dot status-dot--green"></span>
            API Active
          </div>
        </div>
      }
    </div>

    <div class="section-footer">
      <a routerLink="/api-management" class="btn btn--secondary">
        Manage APIs
        <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16">
          <path fill-rule="evenodd" d="M7.293 14.707a1 1 0 010-1.414L10.586 10 7.293 6.707a1 1 0 011.414-1.414l4 4a1 1 0 010 1.414l-4 4a1 1 0 01-1.414 0z" clip-rule="evenodd" />
        </svg>
      </a>
    </div>
  </section>

  <!-- ── Section 3: Security ─────────────────────────────────── -->
  <section class="home__section">
    <div class="section-header">
      <div class="section-header__icon section-header__icon--purple">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="24" height="24">
          <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
        </svg>
      </div>
      <div class="section-header__text">
        <h2>Security</h2>
        <p>Protect your APIs with keys, rate limits, IP rules, and authentication</p>
      </div>
    </div>

    <div class="security-grid">
      <div class="security-card">
        <div class="security-card__top">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="20" height="20">
            <path d="M15 7a2 2 0 012 2m4 0a8 8 0 10-12.28 6.16A8 8 0 0012 16h4a2 2 0 012 2v2a2 2 0 01-2 2h-4a2 2 0 01-2-2v-2z" />
          </svg>
          <span class="security-card__title">API Keys</span>
        </div>
        <p class="security-card__desc">Manage per-workspace API keys, regenerate, and revoke access</p>
        <span class="security-card__status security-card__status--enabled">Enabled</span>
      </div>

      <div class="security-card">
        <div class="security-card__top">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="20" height="20">
            <path d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          <span class="security-card__title">Rate Limiting</span>
        </div>
        <p class="security-card__desc">Set requests-per-minute limits per endpoint to prevent abuse</p>
        <span class="security-card__status security-card__status--enabled">Enabled</span>
      </div>

      <div class="security-card">
        <div class="security-card__top">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="20" height="20">
            <path d="M9 3H5a2 2 0 00-2 2v4m6-6h10a2 2 0 012 2v4M9 3v10m0 0h10M9 13v4m10-4v4M9 21H5a2 2 0 01-2-2v-4m0 6h18" />
          </svg>
          <span class="security-card__title">IP Allowlist</span>
        </div>
        <p class="security-card__desc">Whitelist or block specific IP addresses from accessing your APIs</p>
        <span class="security-card__status security-card__status--disabled">Not configured</span>
      </div>

      <div class="security-card">
        <div class="security-card__top">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="20" height="20">
            <path d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
          </svg>
          <span class="security-card__title">Authentication</span>
        </div>
        <p class="security-card__desc">Choose auth methods: API keys, JWT tokens, or OAuth 2.0</p>
        <span class="security-card__status security-card__status--enabled">Enabled</span>
      </div>

      <div class="security-card">
        <div class="security-card__top">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="20" height="20">
            <path d="M9 17v-2m3 2v-4m3 4v-6m2 10H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
          </svg>
          <span class="security-card__title">Audit Log</span>
        </div>
        <p class="security-card__desc">Track all API requests with filterable timeline and export</p>
        <span class="security-card__status security-card__status--disabled">Not configured</span>
      </div>
    </div>

    <div class="section-footer">
      <a routerLink="/security" class="btn btn--secondary">
        Security Dashboard
        <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16">
          <path fill-rule="evenodd" d="M7.293 14.707a1 1 0 010-1.414L10.586 10 7.293 6.707a1 1 0 011.414-1.414l4 4a1 1 0 010 1.414l-4 4a1 1 0 01-1.414 0z" clip-rule="evenodd" />
        </svg>
      </a>
    </div>
  </section>

</div>
```

**Note:** The `@let` template syntax requires Angular 18+. If the project uses an older Angular version, replace `@let tableCount = ...` with a component property or computed signal.

---

## Task 7: Create home component SCSS

**Files:**
- Create: `FE/src/app/home/home.component.scss`

- [ ] **Step 1: Create full SCSS**

```scss
:host {
  display: block;
}

.home {
  max-width: 1100px;
  margin: 0 auto;
  padding: 2rem 1.5rem 4rem;
  display: flex;
  flex-direction: column;
  gap: 3rem;
  animation: page-in 0.4s cubic-bezier(0.16, 1, 0.3, 1) both;
}

@keyframes page-in {
  from { opacity: 0; transform: translateY(10px); }
  to { opacity: 1; transform: translateY(0); }
}

/* ── Hero ──────────────────────────────────────────────── */
.home__hero {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1.5rem;
  padding: 2rem 2rem;
  background: var(--aae-surface);
  border: 1px solid var(--aae-border);
  border-radius: 16px;
}

.home__hero-text {
  h1 {
    margin: 0 0 0.4rem;
    font-size: 1.75rem;
    font-weight: 700;
    color: var(--aae-text);
    letter-spacing: -0.02em;
  }

  p {
    margin: 0;
    font-size: 0.9rem;
    color: var(--aae-text-muted);
  }
}

.home__hero-actions {
  display: flex;
  gap: 0.75rem;
  flex-shrink: 0;
}

/* ── Section ────────────────────────────────────────────── */
.home__section {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.section-header {
  display: flex;
  align-items: flex-start;
  gap: 1rem;

  &__icon {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 48px;
    height: 48px;
    border-radius: 12px;
    flex-shrink: 0;

    &--green {
      background: rgba(34, 197, 94, 0.15);
      color: var(--aae-success);
    }

    &--blue {
      background: var(--aae-accent-muted);
      color: var(--aae-accent);
    }

    &--purple {
      background: rgba(167, 139, 250, 0.15);
      color: #a78bfa;
    }
  }

  &__text {
    h2 {
      margin: 0 0 0.25rem;
      font-size: 1.25rem;
      font-weight: 700;
      color: var(--aae-text);
      letter-spacing: -0.01em;
    }

    p {
      margin: 0;
      font-size: 0.875rem;
      color: var(--aae-text-muted);
    }
  }
}

/* ── Stats Strip ────────────────────────────────────────── */
.stats-strip {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.stat-pill {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.4rem 0.85rem;
  border: 1px solid var(--aae-border);
  border-radius: 999px;
  background: var(--aae-surface);

  &__value {
    font-size: 0.9rem;
    font-weight: 700;
    color: var(--aae-text);
  }

  &__label {
    font-size: 0.78rem;
    color: var(--aae-text-muted);
  }
}

/* ── Workspace Grid ─────────────────────────────────────── */
.ws-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 1rem;
}

.ws-card {
  display: flex;
  flex-direction: column;
  gap: 0.65rem;
  padding: 1.1rem;
  background: var(--aae-surface);
  border: 1px solid var(--aae-border);
  border-radius: 12px;
  transition: border-color 0.2s, transform 0.2s;

  &:hover {
    border-color: var(--aae-accent);
    transform: translateY(-1px);
  }

  &__header {
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }

  &__db-icon {
    color: var(--aae-accent);
    flex-shrink: 0;
  }

  &__name {
    flex: 1;
    font-weight: 600;
    font-size: 0.95rem;
    color: var(--aae-text);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  &__meta {
    display: flex;
    flex-wrap: wrap;
    gap: 0.4rem;
    font-size: 0.72rem;
    color: var(--aae-text-muted);

    span {
      display: inline-flex;
      align-items: center;
      gap: 0.2rem;
    }
  }

  &__actions {
    margin-top: 0.25rem;
  }
}

.ws-badge {
  font-size: 0.68rem;
  font-weight: 600;
  padding: 0.15rem 0.5rem;
  border-radius: 999px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  flex-shrink: 0;

  &--ready {
    background: rgba(34, 197, 94, 0.15);
    color: var(--aae-success);
  }

  &--restoring {
    background: rgba(245, 158, 11, 0.15);
    color: var(--aae-warning);
  }

  &--error {
    background: rgba(239, 68, 68, 0.15);
    color: var(--aae-danger);
  }

  &--empty {
    background: var(--aae-accent-muted);
    color: var(--aae-accent);
  }
}

.ws-empty {
  grid-column: 1 / -1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
  padding: 2rem;
  background: var(--aae-surface);
  border: 1px dashed var(--aae-border);
  border-radius: 12px;
  text-align: center;
  color: var(--aae-text-muted);
}

/* ── API Workspace List ─────────────────────────────────── */
.api-ws-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.api-ws-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.85rem 1rem;
  background: var(--aae-surface);
  border: 1px solid var(--aae-border);
  border-radius: 10px;

  &__info {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    color: var(--aae-text-muted);
  }

  &__name {
    font-weight: 500;
    color: var(--aae-text);
    font-size: 0.875rem;
  }

  &__tables {
    font-size: 0.75rem;
    color: var(--aae-text-muted);
  }

  &__status {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    font-size: 0.78rem;
    color: var(--aae-success);
  }
}

.status-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;

  &--green { background: var(--aae-success); }
  &--yellow { background: var(--aae-warning); }
  &--red { background: var(--aae-danger); }
}

/* ── Security Grid ──────────────────────────────────────── */
.security-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 1rem;
}

.security-card {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: 1.1rem;
  background: var(--aae-surface);
  border: 1px solid var(--aae-border);
  border-radius: 12px;

  &__top {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    color: var(--aae-text-muted);
  }

  &__title {
    font-weight: 600;
    font-size: 0.875rem;
    color: var(--aae-text);
  }

  &__desc {
    margin: 0;
    font-size: 0.78rem;
    color: var(--aae-text-muted);
    line-height: 1.5;
    flex: 1;
  }

  &__status {
    font-size: 0.72rem;
    font-weight: 600;
    padding: 0.2rem 0.5rem;
    border-radius: 999px;
    align-self: flex-start;

    &--enabled {
      background: rgba(34, 197, 94, 0.15);
      color: var(--aae-success);
    }

    &--disabled {
      background: var(--aae-surface-2);
      color: var(--aae-text-muted);
      border: 1px solid var(--aae-border);
    }
  }
}

/* ── Section Footer ─────────────────────────────────────── */
.section-footer {
  display: flex;
  justify-content: flex-end;

  .btn {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
  }
}

/* ── Buttons (shared) ──────────────────────────────────── */
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 0.4rem;
  height: 2.5rem;
  padding: 0 1.1rem;
  font-family: inherit;
  font-size: 0.875rem;
  font-weight: 500;
  border-radius: 10px;
  border: 1px solid transparent;
  cursor: pointer;
  transition: all 0.2s;
  text-decoration: none;

  &--primary {
    background: var(--aae-success);
    border-color: var(--aae-success);
    color: white;
    box-shadow: 0 2px 8px var(--aae-success);

    &:hover {
      filter: brightness(1.08);
    }
  }

  &--secondary {
    background: var(--aae-surface-2);
    border-color: var(--aae-border);
    color: var(--aae-text-muted);

    &:hover {
      border-color: var(--aae-text-muted);
      color: var(--aae-text);
    }
  }

  &--outline {
    background: transparent;
    border-color: var(--aae-border);
    color: var(--aae-text-muted);
    height: 2rem;
    padding: 0 0.75rem;
    font-size: 0.8rem;

    &:hover {
      border-color: var(--aae-accent);
      color: var(--aae-accent);
    }
  }

  &--sm {
    height: 2rem;
    padding: 0 0.75rem;
    font-size: 0.8rem;
  }
}

/* ── Responsive ────────────────────────────────────────── */
@media (max-width: 768px) {
  .home {
    padding: 1.5rem 1rem 3rem;
    gap: 2rem;
  }

  .home__hero {
    flex-direction: column;
    align-items: flex-start;
    padding: 1.5rem;
  }

  .home__hero-actions {
    width: 100%;
  }

  .home__hero-actions .btn {
    flex: 1;
  }

  .ws-grid {
    grid-template-columns: 1fr;
  }

  .security-grid {
    grid-template-columns: 1fr;
  }
}
```

---

## Task 8: Add pipe for uppercase (or use CSS)

**Files:**
- None — use CSS `text-transform: uppercase` instead of the Angular `uppercase` pipe to avoid adding a pipe declaration

- [ ] **Step 1: Update template**

Replace `{{ ws.dbType | uppercase }}` with `{{ ws.dbType }}` and apply CSS `text-transform: uppercase` to the `.ws-card__meta span` selector in SCSS.

---

## Task 9: Verify build compiles

- [ ] **Step 1: Run build**

```bash
cd FE && npm run build 2>&1 | head -60
```

Expected: BUILD SUCCESS with no errors. If there are Angular version issues (e.g., `@let` not supported), fix in template first.

---

## Self-Review Checklist

1. **Spec coverage:** All three sections (Workspaces, API Management, Security) are implemented with proper icons, descriptions, stats, and CTAs.
2. **Placeholder scan:** No `TBD`, `TODO`, or placeholder content found in tasks 1-9.
3. **Type consistency:** `WorkspaceStore` signals (`workspaces`, `workspaceCount`) used correctly; `Workspace` interface fields (`name`, `status`, `sizeBytes`, `lastBackupDate`, `dbType`, `isActive`) all referenced correctly.
4. **Routing:** `url_after_login` updated, root redirect updated, `/home` route added, shell nav updated.

---

## Commit Strategy

After each task, commit with a descriptive message. Suggested commit order:

1. `feat(home): update url_after_login to /home`
2. `feat(home): add home routes and update app routes`
3. `feat(home): update shell topbar nav with new links`
4. `feat(home): create home component ts/html/scss`
5. `feat(home): verify build compiles`

---

**Plan complete and saved to `docs/superpowers/plans/2026-04-18-home-page-plan.md`. Two execution options:**

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**