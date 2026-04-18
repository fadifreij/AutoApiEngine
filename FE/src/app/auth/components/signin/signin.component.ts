import {
  Component, inject, signal, ChangeDetectionStrategy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, ActivatedRoute } from '@angular/router';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MockAuthService } from '../../services/mock-auth.service';
import { AuthStore } from '../../store/auth.store';

@Component({
  selector: 'app-signin',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule, RouterLink, ReactiveFormsModule,
  ],
  templateUrl:'./signin.component.html',
  styleUrl: './signin.component.scss',
})
export class SigninComponent {
  readonly authStore = inject(AuthStore);
  private readonly authService = inject(MockAuthService);
  private readonly route = inject(ActivatedRoute);

  readonly serverError = signal<string | null>(null);
  readonly sessionExpired = signal(false);
  readonly showPassword = signal(false);
  readonly debugStatus = signal<string | null>(null);

  readonly stats = [
    { value: '10K+', label: 'Databases managed' },
    { value: '99.9%', label: 'Uptime SLA' },
    { value: '<50ms', label: 'Avg. response' },
  ];

  readonly techTags = ['REST', 'GraphQL', 'gRPC', 'WebSocket', 'OAuth 2.0', 'JWT'];
  readonly companies = ['Acme Corp', 'DataForge', 'CloudBridge', 'NexGen', 'Stackline'];

  readonly form = inject(FormBuilder).group({
    email:    ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  constructor() {
    this.route.queryParams.subscribe((p) => {
      if (p['reason'] === 'session_expired') this.sessionExpired.set(true);
    });
  }

  isDirty(field: string): boolean {
    const ctrl = this.form.get(field);
    return !!(ctrl?.invalid && (ctrl.dirty || ctrl.touched));
  }

  submit(): void {
    this.debugStatus.set('submit() called');
    this.form.markAllAsTouched();
    const { email, password } = this.form.getRawValue();

    if (!email || !password) {
      this.debugStatus.set('Empty fields: email=' + email + ' pw=' + password);
      return;
    }

    this.serverError.set(null);
    this.debugStatus.set('Calling login with ' + email + '...');

    this.authService.login({ email, password }).subscribe({
      next: (res) => {
        this.debugStatus.set('Login OK! Navigating to /dashboard...');
      },
      error: (err) => {
        this.debugStatus.set('Login ERROR: ' + (err?.message ?? JSON.stringify(err)));
        this.serverError.set(
          err?.error?.message ?? 'Invalid email or password.'
        );
      },
    });
  }
}
