import { HttpClient } from '@angular/common/http';
import { Component, computed, effect, inject, OnDestroy, signal } from '@angular/core';
import { environment } from '../../../environments/environment';
import { DatabaseProgressService, ProgressEvent } from '../../shared/database-progress.service';
import { WorkspaceStateService } from '../../shared/workspace-state.service';

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

  closePopup(): void {
    this.showPopup.set(false);
    this.operationType.set(null);
    this.step1Progress.set(0);
    this.step2Progress.set(0);
    this.backupFileName = '';
    this.downloadStarted = false;
    this.progressService.stop();
  }

  private async openPopupAndRun(type: 'download' | 'backup'): Promise<void> {
    this.operationType.set(type);
    this.step1Progress.set(0);
    this.step2Progress.set(0);
    this.showPopup.set(true);

    // Connect SignalR first, then call API
    await this.progressService.start();
    this.callBackupApi();
  }

  private openPopup(type: OperationType): void {
    this.operationType.set(type);
    this.step1Progress.set(0);
    this.step2Progress.set(0);
    this.showPopup.set(true);
    this.progressService.start();
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

    this.http.get(
      `${environment.apiUrl}/database/download/${encodeURIComponent(this.backupFileName)}`,
      { responseType: 'blob', withCredentials: true }
    ).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = this.backupFileName;
        document.body.appendChild(a);
        a.click();
        // Delay revocation so the browser can start the download
        setTimeout(() => {
          document.body.removeChild(a);
          URL.revokeObjectURL(url);
        }, 1000);
        this.step2Progress.set(100);
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
    this.progressService.stop();
  }
}
