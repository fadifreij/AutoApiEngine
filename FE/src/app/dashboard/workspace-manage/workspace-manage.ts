import { HttpClient, HttpEventType, HttpParams } from '@angular/common/http';
import { Component, computed, effect, inject, OnDestroy, signal } from '@angular/core';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../shared/auth/auth.service';
import { DatabaseProgressService, ProgressEvent } from '../../shared/database-progress.service';
import { WorkspaceStateService } from '../../shared/workspace-state.service';
import { DdlExecutionResponse } from './ddl-types';

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
  step2Status = signal<'idle' | 'preparing' | 'active' | 'done'>('idle');

  // History download progress
  historyDownloading = signal<string | null>(null);
  historyDownloadProgress = signal(0);
  historyDownloadStatus = signal<'idle' | 'preparing' | 'downloading' | 'done'>('idle');

  // DDL script
  ddlSql = signal('');
  ddlResults = signal<DdlExecutionResponse | null>(null);
  ddlExecuting = signal(false);
  ddlFileName = signal('');

  /** Highlighted HTML string for innerHTML binding */
  highlightedSql = computed(() => this.highlightSql(this.ddlSql()));

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
    const status = this.step2Status();
    const op = this.operationType();

    // Show "Preparing download..." while zipping on server
    if (op === 'download' && status === 'preparing') {
      return 'Preparing Download (Zipping)';
    }

    switch (op) {
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
    // Don't start if already downloading
    if (this.historyDownloading()) return;

    this.historyDownloading.set(fileName);
    this.historyDownloadProgress.set(0);
    this.historyDownloadStatus.set('preparing');

    const workspaceId = this.workspaceState.selectedWorkspaceId();
    const params = new HttpParams().set('workspaceId', workspaceId ?? '');

    this.http.get(
      `${environment.apiUrl}/database/download/${encodeURIComponent(fileName)}`,
      {
        params,
        responseType: 'blob',
        withCredentials: true,
        observe: 'events',
        reportProgress: true
      }
    ).subscribe({
      next: (event) => {
        if (event.type === HttpEventType.DownloadProgress) {
          if (this.historyDownloadStatus() === 'preparing') {
            this.historyDownloadStatus.set('downloading');
          }
          if (event.total && event.total > 0) {
            const percentage = Math.round((event.loaded / event.total) * 100);
            this.historyDownloadProgress.set(percentage);
          } else {
            const estimatedTotal = 10 * 1024 * 1024;
            const percentage = Math.min(95, Math.round((event.loaded / estimatedTotal) * 100));
            this.historyDownloadProgress.set(percentage);
          }
        }

        if (event.type === HttpEventType.Response) {
          this.historyDownloadProgress.set(100);
          this.historyDownloadStatus.set('done');

          const blob = event.body as Blob;
          const headers = event.headers;
          let suggestedName = fileName;
          const cd = headers.get('content-disposition');
          if (cd) {
            const m = /filename\*?=(?:UTF-8'')?"?([^";]+)/i.exec(cd);
            if (m && m[1]) suggestedName = decodeURIComponent(m[1]);
          }
          const ct = headers.get('content-type') || '';
          if (!suggestedName && ct.includes('zip')) suggestedName = 'backup.zip';
          if (suggestedName && suggestedName.toLowerCase().endsWith('.bak') && ct.includes('zip')) {
            suggestedName = suggestedName.replace(/\.bak$/i, '.zip');
          }

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

          // Reset after a short delay to show completion
          setTimeout(() => {
            this.historyDownloading.set(null);
            this.historyDownloadProgress.set(0);
            this.historyDownloadStatus.set('idle');
          }, 1500);
        }
      },
      error: () => {
        this.historyDownloading.set(null);
        this.historyDownloadProgress.set(0);
        this.historyDownloadStatus.set('idle');
      }
    });
  }

  closePopup(): void {
    this.showPopup.set(false);
    this.operationType.set(null);
    this.step1Progress.set(0);
    this.step2Progress.set(0);
    this.step2Status.set('idle');
    this.backupFileName = '';
    this.downloadStarted = false;
    const userId = this.authService.getUserId();
    if (userId) this.progressService.leaveUser(userId);
    this.progressService.stop();
  }

  private async openPopupAndRun(type: 'download' | 'backup'): Promise<void> {
    this.operationType.set(type);
    this.progressService.clear();
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
    this.progressService.clear();
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
        }
        if (this.operationType() === 'download') {
          this.downloadFile();
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

    // Show preparing state while server zips the file
    this.step2Status.set('preparing');
    this.step2Progress.set(5); // Show small progress to indicate activity

    const workspaceId = this.workspaceState.selectedWorkspaceId();
    const params = new HttpParams().set('workspaceId', workspaceId ?? '');

    console.debug('Starting file download (server preparing zip):', this.backupFileName);

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
          // First progress event means server finished zipping, now downloading
          if (this.step2Status() === 'preparing') {
            this.step2Status.set('active');
            console.debug('Download started (zip ready)');
          }

          console.debug('Download progress event:', { loaded: event.loaded, total: event.total });
          if (event.total && event.total > 0) {
            const percentage = Math.round((event.loaded / event.total) * 100);
            this.step2Progress.set(percentage);
          } else {
            // No Content-Length available, show indeterminate progress
            // Estimate based on loaded bytes (assume ~10MB typical backup)
            const estimatedTotal = 10 * 1024 * 1024;
            const percentage = Math.min(95, Math.round((event.loaded / estimatedTotal) * 100));
            this.step2Progress.set(percentage);
          }
        }

        // Handle completed response
        if (event.type === HttpEventType.Response) {
          console.debug('Download completed');
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
          this.step2Status.set('done');
        }
      },
      error: (err) => {
        console.error('Download failed:', err);
        this.step2Progress.set(0);
        this.step2Status.set('idle');
        this.downloadStarted = false;
      }
    });
  }

  async onFileSelected(event: Event): Promise<void> {
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

    await this.openPopup('upload');

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

    // Reset file input so the same file can be selected again
    input.value = '';
  }

  onDdlFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files?.length) return;
    const file = input.files[0];

    if (!file.name.toLowerCase().endsWith('.sql')) {
      alert('Only .sql files are accepted.');
      input.value = '';
      return;
    }

    this.ddlFileName.set(file.name);
    const reader = new FileReader();
    reader.onload = () => {
      const text = reader.result as string;
      if (text) {
        this.ddlSql.set(text);
        this.ddlResults.set(null);
      }
    };
    reader.onerror = () => {
      console.error('Failed to read .sql file:', reader.error);
      alert('Failed to read the file. Please try again.');
    };
    reader.readAsText(file);
    input.value = '';
  }

  executeDdl(): void {
    const sql = this.ddlSql().trim();
    if (!sql) return;

    const workspaceId = this.workspaceState.selectedWorkspaceId();
    if (!workspaceId) return;

    this.ddlExecuting.set(true);
    this.ddlResults.set(null);

    this.http.post<DdlExecutionResponse>(
      `${environment.apiUrl}/database/execute-ddl`,
      { workspaceId, sql },
      { withCredentials: true }
    ).subscribe({
      next: (res) => {
        this.ddlResults.set(res);
        this.ddlExecuting.set(false);
      },
      error: (err) => {
        console.error('DDL execution failed:', err);
        this.ddlResults.set({
          statements: [{
            index: 0,
            sql: sql,
            success: false,
            error: err.error?.message || err.message || 'Unknown error',
            rowsAffected: 0,
            durationMs: 0
          }],
          overallSuccess: false,
          totalStatements: 1,
          succeeded: 0,
          failed: 1
        });
        this.ddlExecuting.set(false);
      }
    });
  }

  clearDdl(): void {
    this.ddlSql.set('');
    this.ddlResults.set(null);
    this.ddlFileName.set('');
  }

  trackByIndex(index: number): number {
    return index;
  }

  onDdlInput(event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    this.ddlSql.set(value);
    this.ddlResults.set(null);
  }

  /** Simple SQL syntax highlighter — escapes HTML then wraps keywords/comments/strings in spans */
  private highlightSql(sql: string): string {
    if (!sql) return '';

    // HTML-escape first
    let html = sql
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');

    // Process line-by-line to handle -- line comments
    const lines = html.split('\n');
    const resultLines = lines.map(line => {
      // Check for -- comment (not inside a string)
      let commentIdx = -1;
      let inStr = false;
      let strChar = '';
      for (let i = 0; i < line.length; i++) {
        const c = line[i];
        if (!inStr && (c === '\'' || c === '&quot;' || c === '"')) {
          inStr = true;
          strChar = c;
        } else if (inStr && c === strChar) {
          inStr = false;
        } else if (!inStr && c === '-' && i + 1 < line.length && line[i + 1] === '-') {
          commentIdx = i;
          break;
        }
      }

      let code = '';
      let comment = '';
      if (commentIdx >= 0) {
        code = line.substring(0, commentIdx);
        comment = line.substring(commentIdx);
      } else {
        code = line;
      }

      // Highlight strings in code portion
      code = code.replace(/'[^']*'/g, '<span class="str">$&</span>');
      code = code.replace(/"[^"]*"/g, '<span class="str">$&</span>');

      // Highlight SQL keywords
      const keywordPattern = /\b(CREATE|TABLE|ALTER|DROP|ADD|COLUMN|INDEX|VIEW|SELECT|INSERT|UPDATE|DELETE|FROM|WHERE|SET|INTO|VALUES|PRIMARY|KEY|FOREIGN|REFERENCES|NOT|NULL|DEFAULT|CHECK|UNIQUE|CONSTRAINT|SERIAL|BIGSERIAL|VARCHAR|INT|INTEGER|BIGINT|SMALLINT|DECIMAL|NUMERIC|BOOLEAN|BOOL|TEXT|DATE|TIMESTAMP|TRIGGER|PROCEDURE|FUNCTION|BEGIN|END|IF|ELSE|THEN|AS|ON|AND|OR|IN|EXISTS|BETWEEN|LIKE|IS|ORDER|BY|GROUP|HAVING|LIMIT|OFFSET|JOIN|INNER|LEFT|RIGHT|OUTER|CROSS|USING|UNION|ALL|DISTINCT|ASC|DESC|CASCADE|RESTRICT|TRUNCATE|DATABASE|SCHEMA|TO|WITH|GRANT|REVOKE|COMMIT|ROLLBACK|CASE|WHEN|ELSE|END|TRUE|FALSE|NO|OF|TYPE|ROWS|RANGE|FETCH|NEXT|ONLY|RECURSIVE|RETURNS|LANGUAGE|IMMUTABLE|STABLE|VOLATILE|CALLED|INPUT|SECURITY|DEFINER|INVOKER|EXECUTE|FUNCTION|PROCEDURE|RETURNS)\b/gi;
      code = code.replace(keywordPattern, '<span class="kw">$1</span>');

      if (comment) {
        code += `<span class="cm">${comment}</span>`;
      }

      return code;
    });

    return resultLines.join('\n');
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


