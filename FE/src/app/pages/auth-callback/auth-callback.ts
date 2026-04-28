import { isPlatformBrowser } from '@angular/common';
import { Component, inject, PLATFORM_ID } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../shared/auth/auth.service';
import { LoadingService } from '../../shared/loading/loading.service';

@Component({
  selector: 'app-auth-callback',
  standalone: true,
  template: '<p>Signing in...</p>'
})
export class AuthCallback {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private authService = inject(AuthService);
  private loading = inject(LoadingService);
  private platformId = inject(PLATFORM_ID);

  constructor() {
    // Only run in the browser — SSR has no query params and no token exchange capability
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    this.loading.show();

    const code = this.route.snapshot.queryParamMap.get('code');
    const error = this.route.snapshot.queryParamMap.get('error');

    if (error) {
      this.loading.hide();
      this.router.navigate(['/'], { queryParams: { error: 'Authentication failed' } });
      return;
    }

    if (code) {
      this.authService.handleCallback(code).subscribe({
        next: (response) => {
          this.loading.hide();
          if (response.success) {
            this.router.navigate(['/app/dashboard']);
          } else {
            this.router.navigate(['/'], { queryParams: { error: response.error } });
          }
        },
        error: () => {
          this.loading.hide();
          this.router.navigate(['/'], { queryParams: { error: 'Authentication failed' } });
        }
      });
    } else {
      this.loading.hide();
      this.router.navigate(['/']);
    }
  }
}