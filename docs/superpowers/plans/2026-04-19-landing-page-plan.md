# Public Landing Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a public-facing landing page at `/` for unauthenticated users, with hero, pricing plans, feature sections, and CTAs. Authenticated users are redirected to `/home`.

**Architecture:** Standalone `LandingComponent` using Angular 19 signals, SCSS with `--aae-*` theme variables, PrimeNG Button. Guest-accessible route outside the auth-gated shell.

**Tech Stack:** Angular 19 (standalone components, signals, OnPush), SCSS with CSS custom properties, PrimeNG Button, Angular Router.

---

## File Map

### New files:
- `FE/src/app/landing/landing.routes.ts` — route config for `/`
- `FE/src/app/landing/landing.component.ts` — standalone component
- `FE/src/app/landing/landing.component.html` — template
- `FE/src/app/landing/landing.component.scss` — styles using `--aae-*` variables

### Modified files:
- `FE/src/app/app.routes.ts` — add landing route with `guestGuard`, update root redirect to `/home` for authenticated users

---

## Task 1: Create landing routes file

**Files:**
- Create: `FE/src/app/landing/landing.routes.ts`

- [ ] **Step 1: Create landing routes**

```typescript
import { Routes } from '@angular/router';
import { guestGuard } from '../core/guards/guest.guard';

export const landingRoutes: Routes = [
  {
    path: '',
    canActivate: [guestGuard],
    loadComponent: () =>
      import('./landing.component').then((m) => m.LandingComponent),
    title: 'Auto API Engine — API & Database Platform',
  },
];
```

---

## Task 2: Update app routes

**Files:**
- Modify: `FE/src/app/app.routes.ts`

- [ ] **Step 1: Update root redirect**

Change from redirecting to `'home'` to redirecting authenticated users via the shell, while the landing page is guest-only at `/`:

```typescript
// Root redirect — authenticated users go to /home (inside shell)
{ path: '', redirectTo: 'home', pathMatch: 'full' },
```

- [ ] **Step 2: Add landing route BEFORE the protected shell**

Add a new top-level entry before the `canActivate: [authGuard]` shell block:

```typescript
// ── Public landing (guest only) ──────────────────────────────────
{
  path: '',
  canActivate: [guestGuard],
  loadChildren: () =>
    import('./landing/landing.routes').then((m) => m.landingRoutes),
},

// ── Protected shell ────────────────────────────────────────────────
{
  path: '',
  canActivate: [authGuard],
  // ... rest unchanged
```

**Note:** Angular routes are matched top-to-bottom. The landing route with `guestGuard` will let unauthenticated users see `/`; authenticated users will be redirected to `/home` by the root redirect before ever matching the landing route.

---

## Task 3: Create landing component TypeScript

**Files:**
- Create: `FE/src/app/landing/landing.component.ts`

- [ ] **Step 1: Create LandingComponent**

```typescript
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';

@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [RouterLink, ButtonModule],
  templateUrl: './landing.component.html',
  styleUrl: './landing.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingComponent {
  protected readonly plans = [
    {
      name: 'Identity Server',
      description: 'Provides a dedicated identity management solution for your application.',
      features: [
        'Identity server integration (Okta, custom)',
        'Authentication management',
        'Secure user authentication and authorization',
      ],
      price: 'Contact us',
      highlighted: false,
    },
    {
      name: 'API Without Hosting Database',
      description: 'Provides API functionality without hosting your database.',
      features: [
        'Custom API with CRUD operations',
        'API key management',
        'Provider support (Okta, IdentityServer)',
      ],
      price: 'Contact us',
      highlighted: false,
    },
    {
      name: 'API With Identity Server',
      description: 'Adds Identity Server functionality to Plan 2.',
      features: [
        'All features of Plan 2',
        'Integrated identity management with Okta or IdentityServer',
      ],
      price: 'Contact us',
      highlighted: true,
    },
    {
      name: 'Full Hosting (API + Database)',
      description: 'Complete solution, including database hosting, full API functionality, and security.',
      features: [
        'Database hosting with backup options',
        'API with full CRUD operations',
        'Full security setup with identity provider choice',
      ],
      price: 'Contact us',
      highlighted: false,
    },
  ];

  protected readonly features = [
    {
      title: 'API Generator',
      description: 'Automatically generate API endpoints with CRUD operations, or customize your own.',
      cta: 'Generate your custom API now',
      icon: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="28" height="28"><path d="M13 10V3L4 14h7v7l9-11h-7z"/></svg>`,
    },
    {
      title: 'Database Management',
      description: 'Upload, download, and backup databases with ease.',
      cta: 'Manage your database easily',
      icon: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="28" height="28"><ellipse cx="12" cy="5" rx="9" ry="3"/><path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"/><path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"/></svg>`,
    },
    {
      title: 'Security',
      description: "Secure your API using your own identity provider, or select from options like Okta or our platform's built-in security services.",
      cta: 'Secure your API today',
      icon: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="28" height="28"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>`,
    },
    {
      title: 'Database Explorer',
      description: 'Explore and query your databases directly within the platform.',
      cta: 'Explore your database seamlessly',
      icon: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" width="28" height="28"><path d="M9 3H5a2 2 0 00-2 2v4m6-6h10a2 2 0 012 2v4M9 3v10m0 0h10M9 13v4m10-4v4M9 21H5a2 2 0 01-2-2v-4m0 6h18"/></svg>`,
    },
  ];

  constructor(private router: Router) {}

  navigateToSignup(): void {
    this.router.navigate(['/auth/signup']);
  }
}
```

---

## Task 4: Create landing component HTML template

**Files:**
- Create: `FE/src/app/landing/landing.component.html`

- [ ] **Step 1: Create full HTML template**

```html
<div class="landing">

  <!-- ── Header / Navigation ─────────────────────────────────── -->
  <header class="landing__header">
    <a routerLink="/" class="landing__logo">
      <i class="pi pi-server"></i>
      <span>Auto API Engine</span>
    </a>
    <nav class="landing__nav">
      <a routerLink="/auth/signin" class="btn btn--ghost">Login</a>
      <a routerLink="/auth/signup" class="btn btn--primary">Get Started</a>
    </nav>
  </header>

  <!-- ── Hero ───────────────────────────────────────────────── -->
  <section class="landing__hero">
    <div class="landing__hero-content">
      <h1 class="landing__hero-title">Unlock Your API and Database Security</h1>
      <p class="landing__hero-subtitle">Easily manage your APIs, databases, and security with our streamlined platform.</p>
      <div class="landing__hero-actions">
        <a routerLink="/auth/signup" class="btn btn--primary btn--lg">
          Start your free trial today
          <svg viewBox="0 0 20 20" fill="currentColor" width="18" height="18">
            <path fill-rule="evenodd" d="M10.293 3.293a1 1 0 011.414 0l6 6a1 1 0 010 1.414l-6 6a1 1 0 01-1.414-1.414L14.586 11H3a1 1 0 110-2h11.586l-4.293-4.293a1 1 0 010-1.414z" clip-rule="evenodd" />
          </svg>
        </a>
        <a routerLink="/auth/signin" class="btn btn--outline btn--lg">Learn more</a>
      </div>
    </div>
  </section>

  <!-- ── Pricing ─────────────────────────────────────────────── -->
  <section class="landing__section landing__pricing">
    <h2 class="landing__section-title">Plans & Pricing</h2>
    <p class="landing__section-subtitle">Choose the plan that fits your needs</p>

    <div class="pricing-grid">
      @for (plan of plans; track plan.name) {
        <div class="pricing-card" [class.pricing-card--highlighted]="plan.highlighted">
          @if (plan.highlighted) {
            <div class="pricing-card__badge">Most Popular</div>
          }
          <h3 class="pricing-card__name">{{ plan.name }}</h3>
          <p class="pricing-card__description">{{ plan.description }}</p>
          <ul class="pricing-card__features">
            @for (feature of plan.features; track feature) {
              <li class="pricing-card__feature">
                <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16">
                  <path fill-rule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clip-rule="evenodd" />
                </svg>
                {{ feature }}
              </li>
            }
          </ul>
          <div class="pricing-card__footer">
            <span class="pricing-card__price">{{ plan.price }}</span>
            <a routerLink="/auth/signup" class="btn btn--primary">Get Started</a>
          </div>
        </div>
      }
    </div>
  </section>

  <!-- ── Features ───────────────────────────────────────────── -->
  <section class="landing__section landing__features">
    <h2 class="landing__section-title">Platform Features</h2>
    <p class="landing__section-subtitle">Everything you need to manage, secure, and scale your APIs</p>

    <div class="features-grid">
      @for (feature of features; track feature.title) {
        <div class="feature-card">
          <div class="feature-card__icon" [innerHTML]="feature.icon"></div>
          <h3 class="feature-card__title">{{ feature.title }}</h3>
          <p class="feature-card__description">{{ feature.description }}</p>
          <a routerLink="/auth/signup" class="feature-card__cta">
            {{ feature.cta }}
            <svg viewBox="0 0 20 20" fill="currentColor" width="14" height="14">
              <path fill-rule="evenodd" d="M7.293 14.707a1 1 0 010-1.414L10.586 10 7.293 6.707a1 1 0 011.414-1.414l4 4a1 1 0 010 1.414l-4 4a1 1 0 01-1.414 0z" clip-rule="evenodd" />
            </svg>
          </a>
        </div>
      }
    </div>
  </section>

  <!-- ── CTA Section ─────────────────────────────────────────── -->
  <section class="landing__section landing__cta-section">
    <h2 class="landing__cta-title">Ready to get started?</h2>
    <p class="landing__cta-subtitle">Join thousands of teams using Auto API Engine to manage their databases and APIs.</p>
    <div class="landing__cta-actions">
      <a routerLink="/auth/signup" class="btn btn--primary btn--lg">Start your free trial today</a>
      <a routerLink="/auth/signin" class="btn btn--outline btn--lg">Contact us for more information</a>
    </div>
  </section>

  <!-- ── Footer ─────────────────────────────────────────────── -->
  <footer class="landing__footer">
    <div class="landing__footer-links">
      <a routerLink="/auth/signin">Login</a>
      <a routerLink="/auth/signup">Sign up</a>
      <a href="#">Terms</a>
      <a href="#">Privacy</a>
    </div>
    <p class="landing__footer-copy">&copy; 2026 Auto API Engine. All rights reserved.</p>
  </footer>

</div>
```

---

## Task 5: Create landing component SCSS

**Files:**
- Create: `FE/src/app/landing/landing.component.scss`

- [ ] **Step 1: Create full SCSS**

```scss
:host {
  display: block;
}

.landing {
  min-height: 100vh;
  background: var(--aae-bg);
  color: var(--aae-text);
  animation: page-in 0.4s cubic-bezier(0.16, 1, 0.3, 1) both;
}

@keyframes page-in {
  from { opacity: 0; transform: translateY(10px); }
  to { opacity: 1; transform: translateY(0); }
}

/* ── Header ────────────────────────────────────────────────── */
.landing__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1.25rem 2rem;
  border-bottom: 1px solid var(--aae-border);
  background: var(--aae-surface);
  position: sticky;
  top: 0;
  z-index: 10;
}

.landing__logo {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-weight: 700;
  font-size: 1.15rem;
  color: var(--aae-text);
  text-decoration: none;

  i {
    color: var(--aae-accent);
    font-size: 1.3rem;
  }
}

.landing__nav {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

/* ── Hero ──────────────────────────────────────────────────── */
.landing__hero {
  display: flex;
  align-items: center;
  justify-content: center;
  text-align: center;
  padding: 6rem 2rem;
  background: linear-gradient(180deg, var(--aae-surface) 0%, var(--aae-bg) 100%);
  border-bottom: 1px solid var(--aae-border);
}

.landing__hero-content {
  max-width: 720px;
}

.landing__hero-title {
  margin: 0 0 1rem;
  font-size: 3rem;
  font-weight: 800;
  color: var(--aae-text);
  letter-spacing: -0.03em;
  line-height: 1.1;

  @media (max-width: 640px) {
    font-size: 2.25rem;
  }
}

.landing__hero-subtitle {
  margin: 0 0 2rem;
  font-size: 1.125rem;
  color: var(--aae-text-muted);
  line-height: 1.6;
}

.landing__hero-actions {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  flex-wrap: wrap;
}

/* ── Section ───────────────────────────────────────────────── */
.landing__section {
  padding: 5rem 2rem;
  max-width: 1100px;
  margin: 0 auto;
}

.landing__section-title {
  margin: 0 0 0.5rem;
  font-size: 2rem;
  font-weight: 700;
  color: var(--aae-text);
  letter-spacing: -0.02em;
  text-align: center;
}

.landing__section-subtitle {
  margin: 0 0 3rem;
  font-size: 1rem;
  color: var(--aae-text-muted);
  text-align: center;
}

/* ── Pricing Grid ──────────────────────────────────────────── */
.pricing-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: 1.5rem;
}

.pricing-card {
  display: flex;
  flex-direction: column;
  padding: 1.75rem;
  background: var(--aae-surface);
  border: 1px solid var(--aae-border);
  border-radius: 16px;
  position: relative;
  transition: border-color 0.2s, transform 0.2s;

  &:hover {
    border-color: var(--aae-accent);
    transform: translateY(-2px);
  }

  &--highlighted {
    border-color: var(--aae-accent);
    box-shadow: 0 4px 20px color-mix(in srgb, var(--aae-accent) 20%, transparent);
  }

  &__badge {
    position: absolute;
    top: -12px;
    left: 50%;
    transform: translateX(-50%);
    background: var(--aae-accent);
    color: #fff;
    font-size: 0.72rem;
    font-weight: 700;
    padding: 0.25rem 0.85rem;
    border-radius: 999px;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    white-space: nowrap;
  }

  &__name {
    margin: 0 0 0.5rem;
    font-size: 1.1rem;
    font-weight: 700;
    color: var(--aae-text);
  }

  &__description {
    margin: 0 0 1.25rem;
    font-size: 0.875rem;
    color: var(--aae-text-muted);
    line-height: 1.5;
    flex: 1;
  }

  &__features {
    list-style: none;
    margin: 0 0 1.5rem;
    padding: 0;
    display: flex;
    flex-direction: column;
    gap: 0.6rem;
  }

  &__feature {
    display: flex;
    align-items: flex-start;
    gap: 0.5rem;
    font-size: 0.825rem;
    color: var(--aae-text-muted);

    svg {
      color: var(--aae-success);
      flex-shrink: 0;
      margin-top: 2px;
    }
  }

  &__footer {
    display: flex;
    flex-direction: column;
    gap: 0.85rem;
    margin-top: auto;
  }

  &__price {
    font-size: 0.8rem;
    font-weight: 600;
    color: var(--aae-text-muted);
    text-transform: uppercase;
    letter-spacing: 0.04em;
  }
}

/* ── Features Grid ─────────────────────────────────────────── */
.features-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: 1.5rem;
}

.feature-card {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  padding: 1.5rem;
  background: var(--aae-surface);
  border: 1px solid var(--aae-border);
  border-radius: 12px;
  transition: border-color 0.2s, transform 0.2s;

  &:hover {
    border-color: var(--aae-accent);
    transform: translateY(-2px);
  }

  &__icon {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 52px;
    height: 52px;
    border-radius: 12px;
    background: var(--aae-accent-muted);
    color: var(--aae-accent);
    margin-bottom: 1rem;
  }

  &__title {
    margin: 0 0 0.5rem;
    font-size: 1rem;
    font-weight: 700;
    color: var(--aae-text);
  }

  &__description {
    margin: 0 0 1rem;
    font-size: 0.875rem;
    color: var(--aae-text-muted);
    line-height: 1.5;
    flex: 1;
  }

  &__cta {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    font-size: 0.825rem;
    font-weight: 600;
    color: var(--aae-accent);
    text-decoration: none;
    transition: gap 0.2s;

    &:hover {
      gap: 0.5rem;
    }
  }
}

/* ── CTA Section ───────────────────────────────────────────── */
.landing__cta-section {
  text-align: center;
  background: var(--aae-surface);
  border-top: 1px solid var(--aae-border);
  border-bottom: 1px solid var(--aae-border);
  border-radius: 16px;
  padding: 4rem 2rem;
  max-width: 900px;
  margin: 0 auto 2rem;
}

.landing__cta-title {
  margin: 0 0 0.75rem;
  font-size: 2rem;
  font-weight: 700;
  color: var(--aae-text);
  letter-spacing: -0.02em;
}

.landing__cta-subtitle {
  margin: 0 0 2rem;
  font-size: 1rem;
  color: var(--aae-text-muted);
}

.landing__cta-actions {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  flex-wrap: wrap;
}

/* ── Footer ─────────────────────────────────────────────────── */
.landing__footer {
  padding: 2rem;
  border-top: 1px solid var(--aae-border);
  text-align: center;
}

.landing__footer-links {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1.5rem;
  margin-bottom: 1rem;
  flex-wrap: wrap;

  a {
    font-size: 0.875rem;
    color: var(--aae-text-muted);
    text-decoration: none;
    transition: color 0.2s;

    &:hover {
      color: var(--aae-text);
    }
  }
}

.landing__footer-copy {
  margin: 0;
  font-size: 0.78rem;
  color: var(--aae-text-muted);
}

/* ── Buttons (shared) ──────────────────────────────────────── */
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 0.4rem;
  height: 2.75rem;
  padding: 0 1.25rem;
  font-family: inherit;
  font-size: 0.9rem;
  font-weight: 600;
  border-radius: 10px;
  border: 1px solid transparent;
  cursor: pointer;
  transition: all 0.2s;
  text-decoration: none;
  white-space: nowrap;

  &--primary {
    background: var(--aae-accent);
    border-color: var(--aae-accent);
    color: #fff;

    &:hover {
      filter: brightness(1.1);
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

  &--ghost {
    background: transparent;
    border-color: transparent;
    color: var(--aae-text-muted);
    height: 2.75rem;

    &:hover {
      color: var(--aae-text);
      background: var(--aae-accent-muted);
    }
  }

  &--outline {
    background: transparent;
    border-color: var(--aae-border);
    color: var(--aae-text);

    &:hover {
      border-color: var(--aae-accent);
      color: var(--aae-accent);
    }
  }

  &--lg {
    height: 3rem;
    padding: 0 1.5rem;
    font-size: 0.95rem;
  }
}

/* ── Responsive ─────────────────────────────────────────────── */
@media (max-width: 768px) {
  .landing__header {
    padding: 1rem 1.25rem;
  }

  .landing__hero {
    padding: 4rem 1.25rem;
  }

  .landing__section {
    padding: 3rem 1.25rem;
  }

  .pricing-grid,
  .features-grid {
    grid-template-columns: 1fr;
  }
}
```

---

## Task 6: Verify build compiles

- [ ] **Step 1: Run build**

```bash
cd FE && npm run build 2>&1 | head -60
```

Expected: BUILD SUCCESS with no errors.

---

## Self-Review Checklist

1. **Spec coverage:** Hero, pricing plans (4), feature sections (4), CTA, footer all implemented.
2. **Guest access:** Landing route uses `guestGuard` — only unauthenticated users can access `/`.
3. **Authenticated redirect:** Root redirect to `/home` sends logged-in users away from landing page.
4. **Theme consistency:** Uses `--aae-*` CSS variables throughout.
5. **No placeholder content:** All text from the spec is present.
6. **Routing:** `/auth/signin` and `/auth/signup` remain accessible to guests via `guestGuard`.

---

## Commit Strategy

After each task, commit with a descriptive message. Suggested commit order:

1. `feat(landing): add landing routes with guestGuard`
2. `feat(landing): update app routes to add landing page at root`
3. `feat(landing): create landing component ts/html/scss`
4. `feat(landing): verify build compiles`

---

**Plan complete and saved to `docs/superpowers/plans/2026-04-19-landing-page-plan.md`. Two execution options:**

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**
