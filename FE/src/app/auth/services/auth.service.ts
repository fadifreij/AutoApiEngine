import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { tap, finalize } from 'rxjs/operators';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthStore, User, Org } from '../store/auth.store';
import { NotificationService } from '../../core/services/notification.service';
import { url_after_login, url_logout } from '../constants';

// ── DTOs (mirror your .NET API) ────────────────────────────────────────────
export interface RegisterDto {
  firstName: string;
  lastName: string;
  organizationName: string;
  email: string;
  password: string;
}

export interface LoginDto {
  email: string;
  password: string;
}

export interface AuthResponse {
  token: string;
  refreshToken: string;
  user: User;
  org: Org;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly authStore = inject(AuthStore);
  private readonly notify = inject(NotificationService);
  private readonly base = `${environment.apiUrl}/auth`;

  // ── Register (create org + admin user) ────────────────────────────────────
  register(dto: RegisterDto): Observable<AuthResponse> {
    this.authStore.setLoading(true);
    return this.http.post<AuthResponse>(`${this.base}/register`, dto).pipe(
      tap((res) => {
        this.authStore.setSession(res.user, res.org, res.token, res.refreshToken);
        this.notify.success('Welcome aboard!', `${res.org.name} is ready.`);
        this.router.navigate([url_after_login]);
      }),
      finalize(() => this.authStore.setLoading(false))
    );
  }

  // ── Login ──────────────────────────────────────────────────────────────────
  login(dto: LoginDto): Observable<AuthResponse> {
    this.authStore.setLoading(true);
    return this.http.post<AuthResponse>(`${this.base}/login`, dto).pipe(
      tap((res) => {
        this.authStore.setSession(res.user, res.org, res.token, res.refreshToken);
        this.router.navigate([url_after_login]);
      }),
      finalize(() => this.authStore.setLoading(false))
    );
  }

  // ── Logout ─────────────────────────────────────────────────────────────────
  logout(): void {
    this.authStore.clear();
    this.router.navigate([url_logout]);
    this.notify.info('Signed out', 'See you next time.');
  }

  // ── Refresh token ──────────────────────────────────────────────────────────
  refresh(refreshToken: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.base}/refresh`, { refreshToken }).pipe(
      tap((res) => {
        this.authStore.setSession(res.user, res.org, res.token, res.refreshToken);
      })
    );
  }
}
