// Mock authentication for development/testing without a backend API
// This allows users to test the protected routes and routing without an actual backend

import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, of } from 'rxjs';
import { tap, delay, finalize } from 'rxjs/operators';
import { AuthStore, User, Org } from '../store/auth.store';

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
export class MockAuthService {
  private readonly router = inject(Router);
  private readonly authStore = inject(AuthStore);

  // Mock login with any email/password
  login(dto: LoginDto): Observable<AuthResponse> {
    this.authStore.setLoading(true);

    // Simulate API delay
    return of({
      token: 'mock_token_' + Date.now(),
      refreshToken: 'mock_refresh_' + Date.now(),
      user: {
        id: 'user-123',
        firstName: dto.email.split('@')[0],
        lastName: 'User',
        email: dto.email,
        role: 'developer' as const,
        avatarUrl: `https://ui-avatars.com/api/?name=${dto.email}`,
      },
      org: {
        id: 'org-123',
        name: 'Test Organization',
        plan: 'pro' as const,
        seats: 10,
      },
    }).pipe(
      delay(500), // Simulate network delay
      tap((res) => {
        this.authStore.setSession(res.user, res.org, res.token, res.refreshToken);
        this.router.navigate(['/dashboard']);
      }),
      finalize(() => this.authStore.setLoading(false))
    );
  }

  // Mock registration
  register(data: any): Observable<AuthResponse> {
    this.authStore.setLoading(true);

    return of({
      token: 'mock_token_' + Date.now(),
      refreshToken: 'mock_refresh_' + Date.now(),
      user: {
        id: 'user-456',
        firstName: data.firstName,
        lastName: data.lastName,
        email: data.email,
        role: 'admin' as const,
        avatarUrl: `https://ui-avatars.com/api/?name=${data.firstName}+${data.lastName}`,
      },
      org: {
        id: 'org-456',
        name: data.organizationName,
        plan: 'trial' as const,
        seats: 5,
      },
    }).pipe(
      delay(500),
      tap((res) => {
        this.authStore.setSession(res.user, res.org, res.token, res.refreshToken);
        this.router.navigate(['/dashboard']);
      }),
      finalize(() => this.authStore.setLoading(false))
    );
  }

  logout(): void {
    this.authStore.clear();
    this.router.navigate(['/auth/signin']);
  }
}
