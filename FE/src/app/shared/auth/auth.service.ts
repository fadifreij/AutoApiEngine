import { HttpClient } from '@angular/common/http';
import { Inject, Injectable, PLATFORM_ID, signal } from '@angular/core';
import { catchError, map, Observable, of, tap } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface RegisterRequest {
  email: string;
  password: string;
  organizationName: string;
}

export interface AuthResponse {
  success: boolean;
  token?: string;
  idToken?: string;
  error?: string;
  refreshToken?: string;
  organizationId?: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly API_URL = `${environment.apiUrl}/auth`;

  private readonly KEYCLOAK_URL = environment.keycloakUrl;
  private readonly CLIENT_ID = environment.clientId;

  accessToken = signal<string | null>(null);
  currentUser = signal<string | null>(null);
  isAuthenticated = signal<boolean>(false);
  organizationId = signal<string | null>(null);

  private idToken: string | null = null;

  private static readonly ID_TOKEN_KEY = 'id_token';
  private static readonly ORG_ID_KEY = 'organization_id';

  constructor(
    private http: HttpClient,
    @Inject(PLATFORM_ID) private platformId: Object
  ) {
    if (this.isBrowser()) {
      this.idToken = localStorage.getItem(AuthService.ID_TOKEN_KEY);
      const savedOrgId = localStorage.getItem(AuthService.ORG_ID_KEY);
      if (savedOrgId) this.organizationId.set(savedOrgId);

      window.addEventListener('storage', (e) => {
        if (e.key === AuthService.ORG_ID_KEY && e.newValue) {
          this.organizationId.set(e.newValue);
        }
        if (e.key !== AuthService.ID_TOKEN_KEY) return;
        if (e.newValue) {
          this.idToken = e.newValue;
        } else {
          this.idToken = null;
          this.accessToken.set(null);
          this.isAuthenticated.set(false);
          this.currentUser.set(null);
          this.organizationId.set(null);
          localStorage.removeItem(AuthService.ORG_ID_KEY);
        }
      });
    }
  }

  private setIdToken(token: string | null): void {
    this.idToken = token;
    if (this.isBrowser()) {
      if (token) localStorage.setItem(AuthService.ID_TOKEN_KEY, token);
      else localStorage.removeItem(AuthService.ID_TOKEN_KEY);
    }
  }

  private setOrganizationId(id: string | null): void {
    this.organizationId.set(id);
    if (this.isBrowser()) {
      if (id) localStorage.setItem(AuthService.ORG_ID_KEY, id);
      else localStorage.removeItem(AuthService.ORG_ID_KEY);
    }
  }

  private isBrowser(): boolean {
    return this.platformId === 'browser';
  }

  initAuth(): Observable<boolean> {
    if (!this.isBrowser()) {
      return of(false);
    }

    const savedIdToken = localStorage.getItem(AuthService.ID_TOKEN_KEY);
    if (!savedIdToken) {
      this.isAuthenticated.set(false);
      return of(false);
    }

    this.idToken = savedIdToken;

    return this.refreshToken().pipe(
      map(res => {
        if (res.success && res.token) {
          this.isAuthenticated.set(true);
          return true;
        } else {
          this.isAuthenticated.set(false);
          return false;
        }
      }),
      catchError(() => {
        this.isAuthenticated.set(false);
        return of(false);
      })
    );
  }

  getKeycloakLoginUrl(): string {
    if (!this.isBrowser()) return '';

    const redirectUri = encodeURIComponent(
      window.location.origin + '/auth/callback'
    );

    return `${this.KEYCLOAK_URL}/realms/ApiEngineRealm/protocol/openid-connect/auth` +
      `?client_id=${this.CLIENT_ID}` +
      `&redirect_uri=${redirectUri}` +
      `&response_type=code` +
      `&scope=openid organization`;
  }

  login(): void {
    if (this.isBrowser()) {
      window.location.replace(this.getKeycloakLoginUrl());
    }
  }

  handleCallback(code: string): Observable<AuthResponse> {
    const redirectUri = window.location.origin + '/auth/callback';

    return this.http.post<any>(
      `${this.API_URL}/Login`,
      { code, redirectUri },
      { withCredentials: true }
    ).pipe(
      map((response: any) => {
        if (response.success) {
          this.accessToken.set(response.token);
          this.setIdToken(response.idToken);
          this.isAuthenticated.set(true);
          if (response.organizationId) this.setOrganizationId(response.organizationId);
          return { success: true, token: response.token, organizationId: response.organizationId } as AuthResponse;
        }
        return { success: false, error: response.error } as AuthResponse;
      })
    );
  }

  register(data: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<any>(
      `${this.API_URL}/register`,
      data
    ).pipe(
      map((res: any) => {
        if (res.success) {
          this.isAuthenticated.set(true);
          this.currentUser.set(data.email);
          if (res.organizationId) this.setOrganizationId(res.organizationId);
          return { success: true, organizationId: res.organizationId } as AuthResponse;
        }
        return { success: false, error: res.error } as AuthResponse;
      })
    );
  }

  logout(): void {
    if (!this.isBrowser()) return;

    this.accessToken.set(null);
    this.isAuthenticated.set(false);
    this.currentUser.set(null);
    this.setOrganizationId(null);

    const postLogoutRedirect = window.location.origin;
    const idTokenHint = this.idToken;
    this.setIdToken(null);
    this.http.post(`${this.API_URL}/logout`, { postLogoutRedirectUri: postLogoutRedirect, idTokenHint }, {
      headers: { 'Content-Type': 'application/json' },
      withCredentials: true
    }).subscribe(
      (res: any) => {
        console.log(res["logoutUrl"]);
        window.location.href = res["logoutUrl"];
      }
    );
  }

  getAccessToken(): string | null {
    return this.accessToken();
  }

  getOrganization(): string | null {
    const token = this.accessToken();
    if (!token) return null;

    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      return payload['organization'] || null;
    } catch {
      return null;
    }
  }

  getOrganizationId(): string | null {
    return this.organizationId();
  }

  refreshToken(): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(
      `${this.API_URL}/refresh-token`,
      {},
      { withCredentials: true }
    ).pipe(
      tap((res: any) => {
        if (res.success && res.token) {
          this.accessToken.set(res.token);
          if (res.idToken) this.setIdToken(res.idToken);
          if (res.organizationId) this.setOrganizationId(res.organizationId);
        }
      })
    );
  }
}
