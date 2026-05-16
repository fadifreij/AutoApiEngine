import { ChangeDetectorRef, Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../shared/auth/auth.service';

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
  private cdr = inject(ChangeDetectorRef);

  dbType = signal<'hosted' | 'external'>('hosted');
  showEncryptionKey = signal(false);

  formData = {
    name: '',
    encryptionKey: '',
    databaseEngine: 'SqlServer',
    host: '',
    userName: '',
    password: ''
  };

  submitting = false;
  error = '';
  showSuccessOverlay = false;
  showFailureOverlay = false;

  get dbEngineOptions(): string[] {
    return ['SqlServer', 'PostgreSql', 'MySql', 'Sqlite'];
  }

  onSubmit(): void {
    if (!this.formData.name) {
      this.error = 'Workspace name is required';
      return;
    }

    this.submitting = true;
    this.error = '';
    this.showSuccessOverlay = false;
    this.showFailureOverlay = false;

    const orgId = this.authService.getOrganizationId();

    const body: any = {
      name: this.formData.name,
      databaseEngine: this.formData.databaseEngine,
      organizationId: orgId
    };

    if (this.dbType() === 'hosted') {
      body.encryptionKey = this.formData.encryptionKey || null;
    } else {
      body.dbUserName = this.formData.userName || null;
      body.dbPassword = this.formData.password || null;
      body.databaseName = this.formData.host || null;
      body.encryptionKey = null;
    }

    this.http.post(`${environment.apiUrl}/workspaces`, body, { withCredentials: true }).subscribe({
      next: () => {
        this.submitting = false;
        this.showSuccessOverlay = true;
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
