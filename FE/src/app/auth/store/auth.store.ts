import { Injectable, signal, computed, inject, PLATFORM_ID } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

export type UserRole = 'admin' | 'developer' | 'viewer';

export interface User {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  role: UserRole;
  avatarUrl?: string;
}

export interface Org {
  id: string;
  name: string;
  plan: 'trial' | 'starter' | 'pro' | 'enterprise';
  seats: number;
}

const TOKEN_KEY = 'aae_token';
const REFRESH_KEY = 'aae_refresh';

@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly platformId = inject(PLATFORM_ID);
  private readonly isBrowser = isPlatformBrowser(this.platformId);

  // ── Private writable signals ───────────────────────────────────────────────
  private readonly _user = signal<User | null>(null);
  private readonly _org = signal<Org | null>(null);
  private readonly _token = signal<string | null>(this.loadToken());
  private readonly _isLoading = signal(false);

  // ── Public readonly signals ────────────────────────────────────────────────
  readonly currentUser = this._user.asReadonly();
  readonly org = this._org.asReadonly();
  readonly token = this._token.asReadonly();
  readonly isLoading = this._isLoading.asReadonly();

  // ── Computed ───────────────────────────────────────────────────────────────
  readonly isAuthenticated = computed(() => !!this._user() && !!this._token());
  readonly isAdmin = computed(() => this._user()?.role === 'admin');
  readonly isDeveloper = computed(() => ['admin', 'developer'].includes(this._user()?.role ?? ''));
  readonly isTrialPlan = computed(() => this._org()?.plan === 'trial');
  readonly fullName = computed(() => {
    const u = this._user();
    return u ? `${u.firstName} ${u.lastName}` : '';
  });

  // ── Mutations ──────────────────────────────────────────────────────────────
  setSession(user: User, org: Org, token: string, refreshToken: string): void {
    this._user.set(user);
    this._org.set(org);
    this._token.set(token);

    if (this.isBrowser) {
      localStorage.setItem(TOKEN_KEY, token);
      localStorage.setItem(REFRESH_KEY, refreshToken);
    }
  }

  updateOrg(partial: Partial<Org>): void {
    this._org.update((o) => (o ? { ...o, ...partial } : o));
  }

  updateUser(partial: Partial<User>): void {
    this._user.update((u) => (u ? { ...u, ...partial } : u));
  }

  setLoading(v: boolean): void {
    this._isLoading.set(v);
  }

  clear(): void {
    this._user.set(null);
    this._org.set(null);
    this._token.set(null);

    if (this.isBrowser) {
      localStorage.removeItem(TOKEN_KEY);
      localStorage.removeItem(REFRESH_KEY);
    }
  }

  // ── Private helpers ────────────────────────────────────────────────────────
  private loadToken(): string | null {
    if (!isPlatformBrowser(this.platformId)) return null;
    return localStorage.getItem(TOKEN_KEY);
  }
}
