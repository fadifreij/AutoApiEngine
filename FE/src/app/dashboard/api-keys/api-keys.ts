import { isPlatformBrowser } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, PLATFORM_ID, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../shared/auth/auth.service';

interface ApiKey {
  id: string;
  name: string;
  key: string;
  expiresAt: string | null;
  lastUsedAt: string | null;
  isActive: boolean;
  organizationId: string;
  createdAt: string;
}

interface CreateApiKeyResponse {
  id: string;
  name: string;
  plainKey: string;
  expiresAt: string | null;
  isActive: boolean;
  organizationId: string;
}

@Component({
  selector: 'app-api-keys',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './api-keys.html',
  styleUrl: './api-keys.scss'
})
export class ApiKeys implements OnInit {
  private http = inject(HttpClient);
  private authService = inject(AuthService);
  private platformId = inject(PLATFORM_ID);

  orgId = this.authService.organizationId;

  keys = signal<ApiKey[]>([]);
  loading = signal(false);
  loaded = signal(false);
  listError = signal('');

  formError = signal('');
  submitting = signal(false);

  // Plain properties (NOT signals) so [(ngModel)] two-way binding works correctly
  // with the template-driven form. Binding ngModel directly to a signal does not
  // update the value at runtime, which would leave the Create button disabled.
  createName = '';
  createExpiresAt = '';

  plainKey = signal('');
  copied = signal(false);

  deletingId = signal<string | null>(null);
  actionMessage = signal('');
  actionError = signal('');

  ngOnInit(): void {
    // Only run in the browser — SSR has no persisted organization id and must not call the API.
    if (!isPlatformBrowser(this.platformId)) return;
    this.loadKeys();
  }

  private loadKeys(): void {
    const orgId = this.orgId();
    if (!orgId) {
      this.loading.set(false);
      this.loaded.set(true);
      this.listError.set('Unable to determine organization.');
      return;
    }

    this.loading.set(true);
    this.loaded.set(false);
    this.listError.set('');

    this.http.get<ApiKey[]>(
      `${environment.apiUrl}/keys/organization/${encodeURIComponent(orgId)}`,
      { withCredentials: true }
    ).subscribe({
      next: (res) => {
        this.keys.set(Array.isArray(res) ? res : []);
        this.loading.set(false);
        this.loaded.set(true);
      },
      error: (err) => {
        this.loading.set(false);
        this.loaded.set(true);
        this.listError.set(this.errorMessage(err, 'Failed to load API keys.'));
      }
    });
  }

  onCreate(): void {
    const name = this.createName.trim();
    const orgId = this.orgId();

    if (!name) {
      this.formError.set('API key name is required.');
      return;
    }
    if (!orgId) {
      this.formError.set('Unable to determine organization.');
      return;
    }

    this.submitting.set(true);
    this.formError.set('');
    this.plainKey.set('');
    this.copied.set(false);

    const expiresAt = this.createExpiresAt
      ? new Date(this.createExpiresAt).toISOString()
      : null;

    this.http.post<CreateApiKeyResponse>(
      `${environment.apiUrl}/keys`,
      { name, organizationId: orgId, expiresAt },
      { withCredentials: true }
    ).subscribe({
      next: (res) => {
        this.submitting.set(false);
        this.plainKey.set(res?.plainKey ?? '');
        this.createName = '';
        this.createExpiresAt = '';
        this.loadKeys();
      },
      error: (err) => {
        this.submitting.set(false);
        this.formError.set(this.errorMessage(err, 'Failed to create API key.'));
      }
    });
  }

  onDelete(item: ApiKey): void {
    if (!confirm(`Are you sure you want to delete "${item.name}"?`)) return;

    this.deletingId.set(item.id);
    this.actionMessage.set('');
    this.actionError.set('');

    this.http.delete<{ message: string }>(
      `${environment.apiUrl}/keys/${encodeURIComponent(item.id)}`,
      { withCredentials: true }
    ).subscribe({
      next: (res) => {
        this.keys.update(list => list.filter(k => k.id !== item.id));
        this.deletingId.set(null);
        this.actionMessage.set(res?.message || `API key "${item.name}" was deleted.`);
      },
      error: (err) => {
        this.deletingId.set(null);
        this.actionError.set(this.errorMessage(err, 'Failed to delete API key.'));
      }
    });
  }

  async copyPlainKey(): Promise<void> {
    const value = this.plainKey();
    if (!value) return;

    try {
      await navigator.clipboard.writeText(value);
      this.copied.set(true);
    } catch {
      // Fallback for browsers/contexts without the async clipboard API
      const ta = document.createElement('textarea');
      ta.value = value;
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      try {
        document.execCommand('copy');
        this.copied.set(true);
      } finally {
        document.body.removeChild(ta);
      }
    }

    setTimeout(() => this.copied.set(false), 2000);
  }

  formatDate(value: string | null): string {
    if (!value) return '—';
    const d = new Date(value);
    if (isNaN(d.getTime())) return '—';
    return d.toLocaleString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  }

  keyPreview(value: string): string {
    if (!value) return '—';
    return value.length > 16 ? `${value.substring(0, 12)}…` : value;
  }

  private errorMessage(err: any, fallback: string): string {
    const body = err?.error;
    return typeof body === 'string'
      ? body
      : body?.message || body?.title || err?.message || fallback;
  }
}