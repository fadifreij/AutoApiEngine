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
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly API_URL = `${environment.apiUrl}/auth`;


  private readonly KEYCLOAK_URL = environment.keycloakUrl;
  private readonly CLIENT_ID = environment.clientId;


  accessToken = signal<string | null>(null);
  currentUser = signal<string | null>(null);
  isAuthenticated = signal<boolean>(false);

  private idToken: string | null = null;

  // Used as a cross-tab hint to avoid spamming refresh-token for anonymous visitors.
  private static readonly ID_TOKEN_KEY = 'id_token';

  constructor(
    private http: HttpClient,
    @Inject(PLATFORM_ID) private platformId: Object
  ) {
    if (this.isBrowser()) {
      this.idToken = localStorage.getItem(AuthService.ID_TOKEN_KEY);

      // Keep tabs in sync: if one tab logs out, others should stop treating the user as authenticated.
      window.addEventListener('storage', (e) => {
        if (e.key !== AuthService.ID_TOKEN_KEY) return;
        if (e.newValue) {
          this.idToken = e.newValue;
        } else {
          this.idToken = null;
          this.accessToken.set(null);
          this.isAuthenticated.set(false);
          this.currentUser.set(null);
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

  private isBrowser(): boolean {
    return this.platformId === 'browser';
  }

  initAuth(): Observable<boolean> {
    if (!this.isBrowser()) {
      return of(false);
    }

    // Only attempt refresh if we have a prior login hint (shared across tabs).
    const savedIdToken = localStorage.getItem(AuthService.ID_TOKEN_KEY);
    if (!savedIdToken) {
      this.isAuthenticated.set(false);
      return of(false);
    }

    // Keep in-memory copy in sync.
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
  // -------------------------
  // LOGIN REDIRECT
  // -------------------------
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

      window.location.href = this.getKeycloakLoginUrl();
    }
  }

  // -------------------------
  // CALLBACK FLOW
  // -------------------------
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
          return { success: true, token: response.token } as AuthResponse;
        }
        return { success: false, error: response.error } as AuthResponse;
      })
    );

  }

  // -------------------------
  // REGISTER
  // -------------------------
  register(data: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(
      `${this.API_URL}/register`,
      data
    ).pipe(
      tap(res => {
        if (res.success) {
          this.isAuthenticated.set(true);
          this.currentUser.set(data.email);
        }
      })
    );
  }

  // -------------------------
  // LOGOUT
  // -------------------------
  logout(): void {
    if (!this.isBrowser()) return;
    // debugger;

    this.accessToken.set(null);
    this.isAuthenticated.set(false);
    this.currentUser.set(null);

    const postLogoutRedirect = window.location.origin;
    const idTokenHint = this.idToken;
    this.setIdToken(null);
    // Clear HTTP-only refresh token cookie via backend
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

  // -------------------------
  // ACCESS TOKEN (memory only)
  // -------------------------
  getAccessToken(): string | null {
    return this.accessToken();
  }

  // -------------------------
  // GET ORGANIZATION FROM TOKEN
  // -------------------------
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

  // -------------------------
  // REFRESH TOKEN (cookie-based backend)
  // -------------------------
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
        }
      })
    );
  }
}
