import { HttpClient } from '@angular/common/http';
import { ChangeDetectorRef, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../shared/auth/auth.service';
import { WorkspaceStateService } from '../../shared/workspace-state.service';

interface TestConnectionResponse {
  success: boolean;
  message: string;
  serverVersion: string | null;
}

interface DbEngineOption {
  value: string;
  label: string;
  icon: SafeHtml;
}

function svgIcon(d: string): SafeHtml {
  return inject(DomSanitizer).bypassSecurityTrustHtml(d);
}

const DB_ENGINE_OPTIONS_RAW = [
  {
    value: 'SqlServer',
    label: 'SQL Server',
    icon: '<svg viewBox="0 0 20 20"><ellipse cx="10" cy="4" rx="7" ry="2.3" fill="#E8A317"/><path d="M3 4v5c0 1.2 3.1 2 7 2s7-.8 7-2V4" fill="#E8A317" opacity=".8"/><path d="M3 7v5c0 1.2 3.1 2 7 2s7-.8 7-2V7" fill="#E8A317" opacity=".55"/><path d="M3 10v5c0 1.2 3.1 2 7 2s7-.8 7-2v-5" fill="#E8A317" opacity=".3"/><circle cx="10" cy="14" r="1.5" fill="#fff" opacity=".7"/></svg>'
  },
  {
    value: 'PostgreSql',
    label: 'PostgreSQL',
    icon: '<svg viewBox="0 0 20 20"><ellipse cx="10" cy="4" rx="7" ry="2.3" fill="#336791"/><path d="M3 4v5c0 1.2 3.1 2 7 2s7-.8 7-2V4" fill="#336791" opacity=".8"/><path d="M3 7v5c0 1.2 3.1 2 7 2s7-.8 7-2V7" fill="#336791" opacity=".55"/><path d="M3 10v5c0 1.2 3.1 2 7 2s7-.8 7-2v-5" fill="#336791" opacity=".3"/><circle cx="7" cy="8.5" r="1" fill="#fff" opacity=".8"/><circle cx="13" cy="8.5" r="1" fill="#fff" opacity=".8"/></svg>'
  },
  {
    value: 'MySql',
    label: 'MySQL',
    icon: '<svg viewBox="0 0 20 20"><ellipse cx="10" cy="4" rx="7" ry="2.3" fill="#4479A1"/><path d="M3 4v5c0 1.2 3.1 2 7 2s7-.8 7-2V4" fill="#4479A1" opacity=".8"/><path d="M3 7v5c0 1.2 3.1 2 7 2s7-.8 7-2V7" fill="#4479A1" opacity=".55"/><path d="M3 10v5c0 1.2 3.1 2 7 2s7-.8 7-2v-5" fill="#4479A1" opacity=".3"/><path d="M6 15c0 1 1.5 2 4 2s4-1 4-2" stroke="#fff" stroke-width=".8" fill="none" opacity=".7"/><circle cx="10" cy="14" r=".6" fill="#fff" opacity=".6"/></svg>'
  },
  {
    value: 'Sqlite',
    label: 'SQLite',
    icon: '<svg viewBox="0 0 20 20"><ellipse cx="10" cy="4" rx="7" ry="2.3" fill="#003B57"/><path d="M3 4v5c0 1.2 3.1 2 7 2s7-.8 7-2V4" fill="#003B57" opacity=".8"/><path d="M3 7v5c0 1.2 3.1 2 7 2s7-.8 7-2V7" fill="#003B57" opacity=".55"/><path d="M3 10v5c0 1.2 3.1 2 7 2s7-.8 7-2v-5" fill="#003B57" opacity=".3"/><path d="M9 12l-1 3 2-1.5-1 3" stroke="#fff" stroke-width=".8" stroke-linecap="round" stroke-linejoin="round" opacity=".7"/></svg>'
  }
];

@Component({
  selector: 'app-workspace-new',
  standalone: true,
  imports: [RouterLink, FormsModule],
  templateUrl: './workspace-new.html',
  styleUrl: './workspace-new.scss'
})
export class WorkspaceNew {
  private http = inject(HttpClient);
  private router = inject(Router);
  private authService = inject(AuthService);
  private workspaceState = inject(WorkspaceStateService);
  private cdr = inject(ChangeDetectorRef);
  private sanitizer = inject(DomSanitizer);

  dbType = signal<'hosted' | 'external'>('hosted');
  showEncryptionKey = signal(false);
  dbEngineOpen = signal(false);

  dbEngineOptions: DbEngineOption[] = DB_ENGINE_OPTIONS_RAW.map(o => ({
    ...o,
    icon: this.sanitizer.bypassSecurityTrustHtml(o.icon)
  }));

  name = signal('');
  nameTouched = signal(false);
  encryptionKeyTouched = signal(false);
  // true when name meets DB identifier rules: lowercase letters, numbers, hyphens; 1-50 chars; cannot start/end with hyphen
  isNameValid = computed(() => {
    const v = (this.name() || '').trim();
    if (!v) return false;
    if (v.length > 50) return false;
    // must start and end with alphanumeric, only allow lowercase letters, numbers, and hyphens
    const re = /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/;
    return re.test(v);
  });

  onNameInput(value: string): void {
    this.name.set(value);
    if (!this.nameTouched()) this.nameTouched.set(true);
    this.refreshCreateDisabled();
  }

  onEncryptionKeyInput(value: string): void {
    this.encryptionKey.set(value);
    if (!this.encryptionKeyTouched()) this.encryptionKeyTouched.set(true);
    this.refreshCreateDisabled();
  }
  encryptionKey = signal('');

  formData = {
    databaseEngine: 'SqlServer',
    serverHost: '',
    databaseName: '',
    userName: '',
    password: ''
  };

  /** Connection test states: idle | testing | success | failed */
  connectionStatus = signal<'idle' | 'testing' | 'success' | 'failed'>('idle');
  connectionMessage = signal('');
  connectionVersion = signal('');

  /** Set to true only when Test Connection succeeds. Reset on any field change or failure. */
  connectionVerified = signal(false);

  /** Whether the Create Workspace button should be disabled — recalculated imperatively. */
  createDisabled = signal(true);

  submitting = false;
  error = '';
  showSuccessOverlay = false;
  showFailureOverlay = false;

  /** Recalculate createDisabled from all dependencies. Called on every relevant change. */
  private refreshCreateDisabled(): void {
  
    if (!this.isNameValid()) {
      this.createDisabled.set(true);
      return;
    }
    if (this.dbType() === 'hosted') {
      this.createDisabled.set(!(this.encryptionKey() || '').trim());
      return;
    }
    console.log(this.connectionStatus())
    this.createDisabled.set(this.connectionStatus() !== 'success');
  }

  setDbType(type: 'hosted' | 'external'): void {
    this.dbType.set(type);
    this.refreshCreateDisabled();
  }

  /** Reset connection test status when form fields change */
  markConnectionIdle(): void {
    if (this.connectionStatus() !== 'idle') {
      this.connectionStatus.set('idle');
      this.connectionMessage.set('');
      this.connectionVersion.set('');
      this.connectionVerified.set(false);
      this.refreshCreateDisabled();
    }
  }

  get selectedEngine(): DbEngineOption | undefined {
    return this.dbEngineOptions.find(e => e.value === this.formData.databaseEngine);
  }

  toggleDbEngine(): void {
    this.dbEngineOpen.update(v => !v);
  }

  selectDbEngine(value: string): void {
    this.formData.databaseEngine = value;
    this.dbEngineOpen.set(false);
    this.markConnectionIdle();
  }

  closeDbEngine(): void {
    this.dbEngineOpen.set(false);
  }

  testConnection(): void {
    if (!this.formData.serverHost.trim()) {
      this.connectionStatus.set('failed');
      this.connectionMessage.set('Server host is required.');
      return;
    }

    this.connectionStatus.set('testing');
    this.connectionMessage.set('');
    this.connectionVersion.set('');

    const body = {
      databaseEngine: this.formData.databaseEngine,
      serverHost: this.formData.serverHost,
      userName: this.formData.userName || null,
      password: this.formData.password || null,
      databaseName: this.formData.databaseName || null
    };

    this.http.post<TestConnectionResponse>(
      `${environment.apiUrl}/workspaces/test-connection`,
      body,
      { withCredentials: true }
    ).subscribe({
      next: (res) => {
        const ok = res.success;
        this.connectionStatus.set(ok ? 'success' : 'failed');
        this.connectionMessage.set(res.message || (ok ? 'Connection successful' : 'Connection failed'));
        if (res.serverVersion) this.connectionVersion.set(res.serverVersion);
        this.connectionVerified.set(ok);
        this.refreshCreateDisabled();
      },
      error: (err) => {
        this.connectionStatus.set('failed');
        const body = err?.error;
        this.connectionMessage.set(
          typeof body === 'string' ? body
            : body?.message || body?.title || err?.message || 'Connection test failed'
        );
        this.connectionVerified.set(false);
        this.refreshCreateDisabled();
      }
    });
  }

  onSubmit(): void {
    if (!this.name()) {
      this.error = 'Workspace name is required';
      return;
    }

    this.submitting = true;
    this.error = '';
    this.showSuccessOverlay = false;
    this.showFailureOverlay = false;

    const orgId = this.authService.getOrganizationId();

    const body: any = {
      name: this.name(),
      organizationId: orgId || undefined
    };

    if (this.dbType() === 'hosted') {
      body.encryptionKey = this.encryptionKey() || null;
    } else {
      body.databaseEngine = this.formData.databaseEngine;
      body.dbUserName = this.formData.userName || null;
      body.dbPassword = this.formData.password || null;
      body.serverHost = this.formData.serverHost || null;
      body.databaseName = this.formData.databaseName || null;
      body.encryptionKey = null;
    }

    this.http.post<any>(`${environment.apiUrl}/workspaces`, body, { withCredentials: true }).subscribe({
      next: (res) => {
        this.submitting = false;
        this.showSuccessOverlay = true;
        if (res?.id && res?.name) {
          this.workspaceState.setSelectedWorkspace(res.id, res.name);
        }
        this.cdr.detectChanges();
        setTimeout(() => this.router.navigate(['/app/dashboard']), 3000);
      },
      error: (err) => {
        this.submitting = false;
        const body = err?.error;
        this.error = typeof body === 'string' ? body
          : body?.message || body?.title || err?.message || 'Failed to create workspace';
        this.showFailureOverlay = true;
        this.cdr.detectChanges();
      }
    });
  }
}
