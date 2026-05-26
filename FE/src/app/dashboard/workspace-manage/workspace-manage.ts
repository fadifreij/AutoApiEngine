import { HttpClient, HttpEventType, HttpParams } from '@angular/common/http';
import { Component, computed, effect, inject, OnDestroy, signal } from '@angular/core';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../shared/auth/auth.service';
import { DatabaseProgressService, ProgressEvent } from '../../shared/database-progress.service';
import { WorkspaceStateService } from '../../shared/workspace-state.service';

interface BackupItem {
  fileName: string;
  sizeBytes: number;
  createdAt: string;
}

type OperationType = 'upload' | 'download' | 'backup' | null;

@Component({
  selector: 'app-workspace-manage',
  standalone: true,
  templateUrl: './workspace-manage.html',
  styleUrl: './workspace-manage.scss'
})
export class WorkspaceManage implements OnDestroy {
  private http = inject(HttpClient);
  private workspaceState = inject(WorkspaceStateService);
  private progressService = inject(DatabaseProgressService);

  workspaceName = computed(() => this.workspaceState.selectedWorkspaceName());
  dbEngineLabel = signal('');
  createdLabel = signal('');
  sizeLabel = signal('');
  lastBackupLabel = signal('');
  backups = signal<BackupItem[]>([]);

  private workspaceEffect = effect(() => {
    const id = this.workspaceState.selectedWorkspaceId();
    if (!id) return;
    this.loadWorkspaceDetails(id);
  });
  private authService = inject(AuthService);

  showPopup = signal(false);
  operationType = signal<OperationType>(null);
  step1Progress = signal(0);
  step2Progress = signal(0);
  private backupFileName = '';
  private downloadStarted = false;

  step1Label = computed(() => {
    switch (this.operationType()) {
      case 'upload': return 'Uploading';
      case 'download': return 'Creating Backup';
      case 'backup': return 'Creating Backup';
      default: return '';
    }
  });

  step2Label = computed(() => {
    switch (this.operationType()) {
      case 'upload': return 'Restoring';
      case 'download': return 'Downloading';
      case 'backup': return 'Verifying';
      default: return '';
    }
  });

  hasStep2 = computed(() => this.operationType() !== 'backup');

  isComplete = computed(() => {
    if (!this.hasStep2()) return this.step1Progress() >= 100;
    return this.step2Progress() >= 100;
  });

  private progressEffect = effect(() => {
    const evt = this.progressService.latestProgress();
    if (!evt) return;
    this.handleProgress(evt);
  });

  private handleProgress(evt: ProgressEvent): void {
    const op = this.operationType();
    if (!op) return;

    if (op === 'download' || op === 'backup') {
      if (evt.operation === 'backup') {
        this.step1Progress.set(evt.percentage);
        if (evt.fileName) {
          this.backupFileName = evt.fileName;
        }
        // Once backup completes and we're in download mode, start file download
        if (evt.percentage >= 100 && op === 'download') {
          this.downloadFile();
        }
      }
    } else if (op === 'upload') {
      if (evt.operation === 'upload') {
        this.step1Progress.set(evt.percentage);
      } else if (evt.operation === 'restore') {
        this.step2Progress.set(evt.percentage);
      }
    }
  }

  startUploadRestore(): void {
    this.openPopup('upload');
    // Upload & restore is handled via file input; will be triggered from the template
  }

  startDownload(): void {
    this.openPopupAndRun('download');
  }

  startBackup(): void {
    this.openPopupAndRun('backup');
  }

  downloadBackup(fileName: string): void {
    const workspaceId = this.workspaceState.selectedWorkspaceId();
    const params = new HttpParams().set('workspaceId', workspaceId ?? '');
    this.http.get(
      `${environment.apiUrl}/database/download/${encodeURIComponent(fileName)}`,
      { params, responseType: 'blob', withCredentials: true, observe: 'response' as const }
    ).subscribe({
      next: (res) => {
        const blob = res.body as Blob;
        const headers = res.headers;
        // Prefer filename from Content-Disposition if provided
        let suggestedName = fileName;
        const cd = headers.get('content-disposition');
        if (cd) {
          const m = /filename\*?=(?:UTF-8'')?"?([^";]+)/i.exec(cd);
          if (m && m[1]) suggestedName = decodeURIComponent(m[1]);
        }
        // If server didn't include a helpful name but returned a zip content-type, force .zip
        const ct = headers.get('content-type') || '';
        if (!suggestedName && ct.includes('zip')) suggestedName = 'backup.zip';
        // If filename exists but has .bak and content is zip, prefer .zip extension
        if (suggestedName && suggestedName.toLowerCase().endsWith('.bak') && ct.includes('zip')) {
          suggestedName = suggestedName.replace(/\.bak$/i, '.zip');
        }

        console.debug('downloadBackup response headers', {
          contentDisposition: headers.get('content-disposition'),
          contentType: headers.get('content-type'),
          blobType: blob?.type
        });

        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = suggestedName || fileName;
        document.body.appendChild(a);
        a.click();
        setTimeout(() => {
          document.body.removeChild(a);
          URL.revokeObjectURL(url);
        }, 1000);
      }
    });
  }

  closePopup(): void {
    this.showPopup.set(false);
    this.operationType.set(null);
    this.step1Progress.set(0);
    this.step2Progress.set(0);
    this.backupFileName = '';
    this.downloadStarted = false;
    const userId = this.authService.getUserId();
    if (userId) this.progressService.leaveUser(userId);
    this.progressService.stop();
  }

  private async openPopupAndRun(type: 'download' | 'backup'): Promise<void> {
    this.operationType.set(type);
    this.step1Progress.set(0);
    this.step2Progress.set(0);
    this.showPopup.set(true);

    // Connect SignalR first, then call API
    await this.progressService.start();
    const userId = this.authService.getUserId();
    if (userId) await this.progressService.joinUser(userId);
    this.callBackupApi();
  }

  private async openPopup(type: OperationType): Promise<void> {
    this.operationType.set(type);
    this.step1Progress.set(0);
    this.step2Progress.set(0);
    this.showPopup.set(true);
    await this.progressService.start();
    const userId = this.authService.getUserId();
    if (userId) await this.progressService.joinUser(userId);
  }

  private callBackupApi(): void {
    const workspaceId = this.workspaceState.selectedWorkspaceId();
    if (!workspaceId) return;

    const usesFallback = !this.progressService.connected();

    // If SignalR isn't connected, simulate progress
    if (usesFallback) {
      this.runFallbackProgress();
    }

    this.http.post<{ message: string; fileName: string }>(
      `${environment.apiUrl}/database/backup`,
      { workspaceId },
      { withCredentials: true }
    ).subscribe({
      next: (res) => {
        this.backupFileName = res.fileName;
        this.refreshLastBackup();
        if (usesFallback) {
          this.stopFallback();
          this.step1Progress.set(100);
          if (this.operationType() === 'download') {
            this.downloadFile();
          }
        }
      },
      error: () => {
        this.stopFallback();
        this.step1Progress.set(0);
      }
    });
  }

  private fallbackTimer: ReturnType<typeof setInterval> | null = null;

  private runFallbackProgress(): void {
    this.stopFallback();
    this.fallbackTimer = setInterval(() => {
      const current = this.step1Progress();
      // Slow down as it approaches 90% (never reaches 100 until API responds)
      if (current < 90) {
        const increment = Math.max(1, Math.floor((90 - current) / 10));
        this.step1Progress.set(Math.min(current + increment, 90));
      }
    }, 300);
  }

  private stopFallback(): void {
    if (this.fallbackTimer) {
      clearInterval(this.fallbackTimer);
      this.fallbackTimer = null;
    }
  }

  private downloadFile(): void {
    if (!this.backupFileName) return;
    if (this.downloadStarted) return;
    this.downloadStarted = true;

    const workspaceId = this.workspaceState.selectedWorkspaceId();
    const params = new HttpParams().set('workspaceId', workspaceId ?? '');

    this.http.get(
      `${environment.apiUrl}/database/download/${encodeURIComponent(this.backupFileName)}`,
      {
        params,
        responseType: 'blob',
        withCredentials: true,
        observe: 'events',
        reportProgress: true
      }
    ).subscribe({
      next: (event) => {
        // Track download progress
        if (event.type === HttpEventType.DownloadProgress) {
          if (event.total) {
            const percentage = Math.round((event.loaded / event.total) * 100);
            this.step2Progress.set(percentage);
          }
        }

        // Handle completed response
        if (event.type === HttpEventType.Response) {
          const blob = event.body as Blob;
          const headers = event.headers;
          const disposition = headers.get('content-disposition');
          let downloadName = this.backupFileName;

          if (disposition) {
            const match = disposition.match(/filename\*?=(?:UTF-8'')?([^;\s]+)/i);
            if (match) downloadName = match[1].replace(/['"]/g, '');
          }

          // If content-type is zip but filename is .bak, change extension to .zip
          const contentType = headers.get('content-type') || '';
          if (downloadName.toLowerCase().endsWith('.bak') && contentType.includes('zip')) {
            downloadName = downloadName.replace(/\.bak$/i, '.zip');
          }

          console.debug('downloadFile response headers', {
            contentDisposition: headers.get('content-disposition'),
            contentType: headers.get('content-type'),
            blobType: blob?.type,
            finalDownloadName: downloadName
          });

          const url = URL.createObjectURL(blob);
          const a = document.createElement('a');
          a.href = url;
          a.download = downloadName;
          document.body.appendChild(a);
          a.click();
          setTimeout(() => {
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
          }, 1000);
          this.step2Progress.set(100);
        }
      },
      error: () => {
        this.step2Progress.set(0);
        this.downloadStarted = false;
      }
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files?.length) return;
    const file = input.files[0];

    // Validate file extension - only .bak or .zip allowed
    const fileName = file.name.toLowerCase();
    if (!fileName.endsWith('.bak') && !fileName.endsWith('.zip')) {
      alert('Only .bak or .zip files are allowed for restore.');
      input.value = ''; // Clear the input
      return;
    }

    const workspaceId = this.workspaceState.selectedWorkspaceId();
    if (!workspaceId) return;

    this.openPopup('upload');

    const formData = new FormData();
    formData.append('workspaceId', workspaceId);
    formData.append('file', file);

    this.http.post(
      `${environment.apiUrl}/database/restore`,
      formData,
      { withCredentials: true }
    ).subscribe({
      error: () => {
        this.step1Progress.set(0);
      }
    });
  }

  ngOnDestroy(): void {
    const userId = this.authService.getUserId();
    if (userId) this.progressService.leaveUser(userId);
    this.progressService.stop();
  }

  private loadWorkspaceDetails(id: string): void {
    this.http.get<any>(`${environment.apiUrl}/workspaces/${id}`).subscribe({
      next: (ws) => {
        const engine = ws.databaseEngine ?? ws.DatabaseEngine ?? ws.DatabaseEngine;
        this.dbEngineLabel.set(this.engineToLabel(engine));

        const created = ws.createdAt ?? ws.CreatedAt;
        if (created) {
          try {
            const d = new Date(created);
            const label = `Created ${d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })}`;
            this.createdLabel.set(label);
          } catch {
            this.createdLabel.set('');
          }
        } else {
          this.createdLabel.set('');
        }

        // Fetch stats for size
        this.http.get<any>(`${environment.apiUrl}/workspaces/${id}/stats`).subscribe({
          next: (stats) => {
            if (stats) {
              if (typeof stats.databaseSizeBytes === 'number') {
                this.sizeLabel.set(this.formatBytes(stats.databaseSizeBytes));
              }
              if (stats.backupHistory && Array.isArray(stats.backupHistory)) {
                this.backups.set(stats.backupHistory);
              }
            } else {
              this.sizeLabel.set('');
            }
          },
          error: () => this.sizeLabel.set('')
        });

        // Fetch last backup info
        this.http.get<any>(`${environment.apiUrl}/database/last-backup/${id}`).subscribe({
          next: (b) => {
            if (b && b.createdAt) {
              try {
                const d = new Date(b.createdAt);
                this.lastBackupLabel.set(this.timeAgo(d));
                if (b.size) this.sizeLabel.set(this.formatBytes(b.size));
              } catch {
                this.lastBackupLabel.set('');
              }
            } else {
              this.lastBackupLabel.set('');
            }
          },
          error: () => this.lastBackupLabel.set('')
        });
      },
      error: () => {
        this.dbEngineLabel.set('');
        this.createdLabel.set('');
        this.sizeLabel.set('');
        this.lastBackupLabel.set('');
      }
    });
  }

  private refreshLastBackup(): void {
    const id = this.workspaceState.selectedWorkspaceId();
    if (!id) return;
    this.http.get<any>(`${environment.apiUrl}/database/last-backup/${id}`).subscribe({
      next: (b) => {
        if (b && b.createdAt) {
          try {
            const d = new Date(b.createdAt);
            this.lastBackupLabel.set(this.timeAgo(d));
            if (b.size) this.sizeLabel.set(this.formatBytes(b.size));
          } catch {
            this.lastBackupLabel.set('');
          }
        }
      },
      error: () => { }
    });
  }

  formatBytes(bytes: number): string {
    if (!bytes) return '0 B';
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(1024));
    return `${(bytes / Math.pow(1024, i)).toFixed(i ? 1 : 0)} ${sizes[i]}`;
  }

  timeAgo(d: Date | string): string {
    if (typeof d === 'string') {
      d = new Date(d);
    }
    const sec = Math.floor((Date.now() - d.getTime()) / 1000);
    if (sec < 60) return `${sec}s ago`;
    const min = Math.floor(sec / 60);
    if (min < 60) return `${min}m ago`;
    const hr = Math.floor(min / 60);
    if (hr < 24) return `${hr}h ago`;
    const days = Math.floor(hr / 24);
    return `${days}d ago`;
  }

  private engineToLabel(engine: any): string {
    if (engine == null) return '';
    if (typeof engine === 'number') {
      switch (engine) {
        case 0: return 'SQL Server';
        case 1: return 'PostgreSQL';
        case 2: return 'MySQL';
        case 3: return 'SQLite';
        default: return 'Unknown';
      }
    }
    if (typeof engine === 'string') {
      const e = engine.toLowerCase();
      if (e.includes('postgres')) return 'PostgreSQL';
      if (e.includes('mysql')) return 'MySQL';
      if (e.includes('sqlserver') || e.includes('sql server')) return 'SQL Server';
      if (e.includes('sqlite')) return 'SQLite';
      return engine;
    }
    return String(engine);
  }
}


