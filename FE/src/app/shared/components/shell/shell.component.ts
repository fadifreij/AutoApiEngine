import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { AuthStore } from '../../../auth/store/auth.store';
import { ThemeSwitcherComponent } from '../../../core/theme/theme-switcher/theme-switcher.component';
import { WorkspaceStore } from '../../../workspace/store/workspace.store';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ButtonModule, ThemeSwitcherComponent, ReactiveFormsModule],
  template: `
    <div class="shell">
      <header class="shell__topbar">
        <div class="shell__topbar-left">
          <p-button
            icon="pi pi-bars"
            [text]="true"
            [rounded]="true"
            severity="secondary"
            (onClick)="store.toggleSidebar()"
            ariaLabel="Toggle sidebar"
          />
          <a routerLink="/dashboard" class="shell__logo">
            <i class="pi pi-server"></i>
            <span>API Engine</span>
          </a>
        </div>

        <nav class="shell__topbar-nav">
          <a routerLink="/home" routerLinkActive="active" class="shell__nav-link">
            <i class="pi pi-home"></i>
            <span>Home</span>
          </a>
          <a routerLink="/workspaces/dashboard" routerLinkActive="active" class="shell__nav-link">
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

        <div class="shell__topbar-right">
          <app-theme-switcher />

          @if (authStore.isAuthenticated()) {
            <div class="profile-container">
              @if (authStore.org(); as org) {
                <span class="shell__org-pill" aria-label="Organization: {{ org.name }}">{{ org.name }}</span>
              }

              <div class="profile-menu">
                <button
                  class="profile-trigger"
                  type="button"
                  (click)="toggleProfile()"
                  [attr.aria-expanded]="profileOpen()"
                  aria-haspopup="true"
                  aria-label="Profile menu"
                >
                  <span class="profile-avatar" aria-hidden="true">{{ initials() }}</span>
                  <span class="profile-name">{{ authStore.fullName() }}</span>
                  <i class="pi pi-chevron-down profile-chevron" [class.is-open]="profileOpen()" aria-hidden="true"></i>
                </button>

                @if (profileOpen()) {
                  <div class="profile-dropdown" role="menu" aria-label="Profile options">

                    <div class="profile-dropdown__header">
                      <span class="profile-dropdown__avatar" aria-hidden="true">{{ initials() }}</span>
                      <div class="profile-dropdown__info">
                        <span class="profile-dropdown__fullname">{{ authStore.fullName() }}</span>
                        @if (authStore.org(); as org) {
                          <span class="profile-dropdown__org">{{ org.name }}</span>
                        }
                      </div>
                    </div>

                    <div class="profile-dropdown__sep" role="separator"></div>

                    @if (!editingName()) {
                      <button
                        class="profile-dropdown__item"
                        type="button"
                        role="menuitem"
                        (click)="startEditName()"
                      >
                        <i class="pi pi-pencil" aria-hidden="true"></i>
                        <span>Change Name</span>
                      </button>

                      <div class="profile-dropdown__sep" role="separator"></div>

                      <button
                        class="profile-dropdown__item profile-dropdown__item--danger"
                        type="button"
                        role="menuitem"
                        (click)="logout()"
                      >
                        <i class="pi pi-sign-out" aria-hidden="true"></i>
                        <span>Logout</span>
                      </button>
                    } @else {
                      <form class="profile-dropdown__edit" [formGroup]="nameForm" (ngSubmit)="saveName()">
                        <span class="profile-dropdown__edit-title">Change Name</span>
                        <div class="profile-field">
                          <label class="profile-field__label" for="shell-firstName">First Name</label>
                          <input
                            id="shell-firstName"
                            class="profile-field__input"
                            type="text"
                            formControlName="firstName"
                            autocomplete="given-name"
                          />
                        </div>
                        <div class="profile-field">
                          <label class="profile-field__label" for="shell-lastName">Last Name</label>
                          <input
                            id="shell-lastName"
                            class="profile-field__input"
                            type="text"
                            formControlName="lastName"
                            autocomplete="family-name"
                          />
                        </div>
                        <div class="profile-dropdown__edit-actions">
                          <button type="button" class="profile-btn profile-btn--ghost" (click)="cancelEdit()">Cancel</button>
                          <button type="submit" class="profile-btn profile-btn--primary" [disabled]="nameForm.invalid">Save</button>
                        </div>
                      </form>
                    }
                  </div>
                }
              </div>
            </div>
          }
        </div>
      </header>

      <div class="shell__body">
        <router-outlet></router-outlet>
      </div>
    </div>

    @if (profileOpen()) {
      <div class="profile-backdrop" (click)="closeProfile()" aria-hidden="true"></div>
    }
  `,
  styles: [`
    .shell {
      display: flex;
      flex-direction: column;
      height: 100vh;
      background: var(--aae-bg);
      color: var(--aae-text);
    }

    /* ── Top bar ── */
    .shell__topbar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 0 1rem;
      height: 56px;
      background: var(--aae-surface);
      border-bottom: 1px solid var(--aae-border);
      flex-shrink: 0;
      z-index: 10;
    }

    .shell__topbar-left {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }

    .shell__logo {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-weight: 600;
      font-size: 1.1rem;
      color: var(--aae-text);
      text-decoration: none;
      i { color: var(--aae-accent); }
    }

    .shell__topbar-nav {
      display: flex;
      align-items: center;
      gap: 0.25rem;
      flex: 1;
      justify-content: center;
    }

    .shell__nav-link {
      display: flex;
      align-items: center;
      gap: 0.4rem;
      padding: 0.5rem 0.85rem;
      border-radius: 8px;
      color: var(--aae-text-muted);
      text-decoration: none;
      font-size: 0.875rem;
      font-weight: 500;
      transition: all 150ms ease;
      &:hover { background: var(--aae-accent-muted); color: var(--aae-text); }
      &.active { background: var(--aae-accent-muted); color: var(--aae-accent); }
    }

    .shell__topbar-right {
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }

    .shell__body {
      flex: 1;
      overflow: hidden;
    }

    /* ── Org pill ── */
    .shell__org-pill {
      display: inline-flex;
      align-items: center;
      padding: 0.3rem 0.75rem;
      border-radius: 999px;
      border: 1px solid var(--aae-border);
      background: color-mix(in srgb, var(--aae-accent) 10%, transparent);
      color: var(--aae-text-muted);
      font-size: 0.78rem;
      font-weight: 600;
      letter-spacing: 0.01em;
      white-space: nowrap;
      max-width: 200px;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    /* ── Profile container ── */
    .profile-container {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }

    /* ── Profile menu & trigger ── */
    .profile-menu {
      position: relative;
    }

    .profile-trigger {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      padding: 0.3rem 0.6rem 0.3rem 0.3rem;
      border: 1px solid transparent;
      border-radius: 999px;
      background: transparent;
      color: var(--aae-text);
      cursor: pointer;
      font-size: 0.875rem;
      font-weight: 500;
      transition: all 150ms ease;

      &:hover {
        background: var(--aae-accent-muted);
        border-color: var(--aae-border);
      }

      &:focus-visible {
        outline: 2px solid var(--aae-accent);
        outline-offset: 2px;
      }
    }

    .profile-avatar {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 30px;
      height: 30px;
      border-radius: 50%;
      background: var(--aae-accent);
      color: #fff;
      font-size: 0.72rem;
      font-weight: 700;
      flex-shrink: 0;
    }

    .profile-name {
      max-width: 140px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .profile-chevron {
      font-size: 0.7rem;
      color: var(--aae-text-muted);
      transition: transform 200ms ease;
      &.is-open { transform: rotate(180deg); }
    }

    /* ── Dropdown panel ── */
    .profile-dropdown {
      position: absolute;
      top: calc(100% + 8px);
      right: 0;
      min-width: 240px;
      background: var(--aae-surface);
      border: 1px solid var(--aae-border);
      border-radius: 12px;
      box-shadow: 0 8px 24px rgba(0,0,0,0.14);
      z-index: 1000;
      overflow: hidden;
      animation: dropdown-in 120ms ease;
    }

    @keyframes dropdown-in {
      from { opacity: 0; transform: translateY(-6px); }
      to   { opacity: 1; transform: translateY(0); }
    }

    .profile-dropdown__header {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      padding: 0.9rem 1rem;
    }

    .profile-dropdown__avatar {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 36px;
      height: 36px;
      border-radius: 50%;
      background: var(--aae-accent);
      color: #fff;
      font-size: 0.8rem;
      font-weight: 700;
      flex-shrink: 0;
    }

    .profile-dropdown__info {
      display: flex;
      flex-direction: column;
      gap: 0.1rem;
      min-width: 0;
    }

    .profile-dropdown__fullname {
      font-weight: 600;
      font-size: 0.9rem;
      color: var(--aae-text);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .profile-dropdown__org {
      font-size: 0.75rem;
      color: var(--aae-text-muted);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .profile-dropdown__sep {
      height: 1px;
      background: var(--aae-border);
      margin: 0;
      border: none;
    }

    .profile-dropdown__item {
      display: flex;
      align-items: center;
      gap: 0.6rem;
      width: 100%;
      padding: 0.65rem 1rem;
      background: transparent;
      border: none;
      color: var(--aae-text);
      font-size: 0.875rem;
      text-align: left;
      cursor: pointer;
      transition: background 120ms ease;

      &:hover { background: var(--aae-accent-muted); }

      &:focus-visible {
        outline: none;
        background: var(--aae-accent-muted);
      }

      &--danger {
        color: #e05c5c;
        &:hover { background: color-mix(in srgb, #e05c5c 12%, transparent); }
      }

      i { font-size: 0.85rem; width: 16px; text-align: center; }
    }

    /* ── Name edit form ── */
    .profile-dropdown__edit {
      display: flex;
      flex-direction: column;
      gap: 0.65rem;
      padding: 0.9rem 1rem;
    }

    .profile-dropdown__edit-title {
      font-size: 0.8rem;
      font-weight: 600;
      color: var(--aae-text-muted);
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .profile-field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }

    .profile-field__label {
      font-size: 0.78rem;
      font-weight: 500;
      color: var(--aae-text-muted);
    }

    .profile-field__input {
      width: 100%;
      padding: 0.4rem 0.6rem;
      border: 1px solid var(--aae-border);
      border-radius: 6px;
      background: var(--aae-bg);
      color: var(--aae-text);
      font-size: 0.875rem;
      outline: none;
      transition: border-color 150ms ease;
      box-sizing: border-box;

      &:focus { border-color: var(--aae-accent); }
    }

    .profile-dropdown__edit-actions {
      display: flex;
      justify-content: flex-end;
      gap: 0.5rem;
      margin-top: 0.25rem;
    }

    .profile-btn {
      padding: 0.35rem 0.85rem;
      border-radius: 6px;
      font-size: 0.82rem;
      font-weight: 500;
      cursor: pointer;
      border: 1px solid transparent;
      transition: all 120ms ease;

      &--ghost {
        background: transparent;
        border-color: var(--aae-border);
        color: var(--aae-text-muted);
        &:hover { border-color: var(--aae-text-muted); color: var(--aae-text); }
      }

      &--primary {
        background: var(--aae-accent);
        color: #fff;
        &:hover { opacity: 0.88; }
        &:disabled { opacity: 0.45; cursor: not-allowed; }
      }
    }

    /* ── Backdrop (closes dropdown on outside click) ── */
    .profile-backdrop {
      position: fixed;
      inset: 0;
      z-index: 999;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShellComponent {
  protected readonly store = inject(WorkspaceStore);
  protected readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  protected readonly profileOpen = signal(false);
  protected readonly editingName = signal(false);

  protected readonly initials = computed(() => {
    const u = this.authStore.currentUser();
    if (!u) return '?';
    return `${u.firstName[0] ?? ''}${u.lastName[0] ?? ''}`.toUpperCase();
  });

  protected readonly nameForm = this.fb.group({
    firstName: ['', [Validators.required, Validators.minLength(1)]],
    lastName: ['', [Validators.required, Validators.minLength(1)]],
  });

  toggleProfile(): void {
    this.profileOpen.update(v => !v);
    if (!this.profileOpen()) {
      this.editingName.set(false);
    }
  }

  closeProfile(): void {
    this.profileOpen.set(false);
    this.editingName.set(false);
  }

  startEditName(): void {
    const u = this.authStore.currentUser();
    if (u) {
      this.nameForm.setValue({ firstName: u.firstName, lastName: u.lastName });
    }
    this.editingName.set(true);
  }

  cancelEdit(): void {
    this.editingName.set(false);
  }

  saveName(): void {
    if (this.nameForm.invalid) return;
    const raw = this.nameForm.getRawValue();
    this.authStore.updateUser({
      firstName: (raw.firstName ?? '').trim(),
      lastName: (raw.lastName ?? '').trim(),
    });
    this.editingName.set(false);
  }

  logout(): void {
    this.authStore.clear();
    this.router.navigate(['/auth/signin']);
  }
}
