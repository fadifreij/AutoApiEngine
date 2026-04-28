import { HttpClient } from '@angular/common/http';
import { Inject, Injectable, PLATFORM_ID, signal } from '@angular/core';
import { Router } from '@angular/router';
import { map, Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface RegisterRequest {
  email: string;
  password: string;
  organizationName: string;
}

export interface AuthResponse {
  success: boolean;
  token?: string;
  refreshToken?: string;
  keycloakToken?: string;
  error?: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly API_URL = environment.apiUrl;
  private readonly KEYCLOAK_URL = environment.keycloakUrl;
  private readonly CLIENT_ID = environment.clientId;

  currentUser = signal<string | null>(null);
  isAuthenticated = signal<boolean>(false);

  constructor(
    private http: HttpClient,
    private router: Router,
    @Inject(PLATFORM_ID) private platformId: Object
  ) {
    this.checkAuth();
  }

  private isBrowser(): boolean {
    return this.platformId === 'browser';
  }

  private checkAuth(): void {
    if (this.isBrowser()) {
      const token = localStorage.getItem('accessToken');
      if (token) {
        this.isAuthenticated.set(true);
      }
    }
  }

  getKeycloakLoginUrl(): string {
    if (!this.isBrowser()) {
      return '';
    }
    const redirectUri = encodeURIComponent(window.location.origin + '/auth/callback');
    return `${this.KEYCLOAK_URL}/realms/ApiEngineRealm/protocol/openid-connect/auth?client_id=${this.CLIENT_ID}&redirect_uri=${redirectUri}&response_type=code&scope=openid`;
  }

  login(): void {
    if (this.isBrowser()) {
      window.location.href = this.getKeycloakLoginUrl();
    }
  }

  handleCallback(code: string): Observable<AuthResponse> {
    if (!this.isBrowser()) {
      return new Observable(obs => obs.next({ success: false, error: 'SSR' }));
    }
    const redirectUri = window.location.origin + '/auth/callback';
    const body = new URLSearchParams();
    body.set('grant_type', 'authorization_code');
    body.set('code', code);
    body.set('redirect_uri', redirectUri);
    body.set('client_id', this.CLIENT_ID);

    return this.http.post<any>(
      `${this.KEYCLOAK_URL}/realms/ApiEngineRealm/protocol/openid-connect/token`,
      body.toString(),
      { headers: { 'Content-Type': 'application/x-www-form-urlencoded' } }
    ).pipe(
      tap((response: any) => {
        localStorage.setItem('accessToken', response.access_token || '');
        localStorage.setItem('refreshToken', response.refresh_token || '');
        localStorage.setItem('idToken', response.id_token || '');
        this.isAuthenticated.set(true);
      }),
      map((response: any) => ({
        success: true,
        token: response.access_token,
        refreshToken: response.refresh_token,
        keycloakToken: response.id_token
      } as AuthResponse))
    );
  }

  register(data: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.API_URL}/register`, data).pipe(
      tap(response => {
        if (response.success && this.isBrowser()) {
          localStorage.setItem('accessToken', response.token || '');
          localStorage.setItem('refreshToken', response.refreshToken || '');
          this.isAuthenticated.set(true);
          this.currentUser.set(data.email);
        }
      })
    );
  }

  logout(): void {
    if (this.isBrowser()) {
      const idToken = localStorage.getItem('idToken');
      localStorage.removeItem('accessToken');
      localStorage.removeItem('refreshToken');
      localStorage.removeItem('idToken');
      this.isAuthenticated.set(false);
      this.currentUser.set(null);

      const postLogoutRedirect = encodeURIComponent(window.location.origin);
      const logoutUrl = idToken
        ? `${this.KEYCLOAK_URL}/realms/ApiEngineRealm/protocol/openid-connect/logout?id_token_hint=${idToken}&post_logout_redirect_uri=${postLogoutRedirect}`
        : `${this.KEYCLOAK_URL}/realms/ApiEngineRealm/protocol/openid-connect/logout?post_logout_redirect_uri=${postLogoutRedirect}&client_id=${this.CLIENT_ID}`;
      window.location.href = logoutUrl;
    }
  }

  getKeycloakToken(): string | null {
    if (!this.isBrowser()) {
      return null;
    }
    return localStorage.getItem('keycloakToken');
  }

  getAccessToken(): string | null {
    if (!this.isBrowser()) {
      return null;
    }
    return localStorage.getItem('accessToken');
  }

  refreshToken(): Observable<AuthResponse> {
    if (!this.isBrowser()) {
      return new Observable(obs => obs.next({ success: false }));
    }
    const refreshToken = localStorage.getItem('refreshToken');
    return this.http.post<AuthResponse>(`${this.API_URL}/refresh-token`, { refreshToken }).pipe(
      tap(response => {
        if (response.success) {
          localStorage.setItem('accessToken', response.token || '');
          localStorage.setItem('refreshToken', response.refreshToken || '');
        }
      })
    );
  }
}
