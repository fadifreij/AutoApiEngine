import { Component, signal, computed } from '@angular/core';

type OperationType = 'upload' | 'download' | 'backup' | null;

@Component({
  selector: 'app-workspace-manage',
  standalone: true,
  templateUrl: './workspace-manage.html',
  styleUrl: './workspace-manage.scss'
})
export class WorkspaceManage {
  showPopup = signal(false);
  operationType = signal<OperationType>(null);
  step1Progress = signal(0);
  step2Progress = signal(0);

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

  private animationTimer: ReturnType<typeof setInterval> | null = null;

  startUploadRestore(): void {
    this.openPopup('upload');
  }

  startDownload(): void {
    this.openPopup('download');
  }

  startBackup(): void {
    this.openPopup('backup');
  }

  closePopup(): void {
    this.stopAnimation();
    this.showPopup.set(false);
    this.operationType.set(null);
    this.step1Progress.set(0);
    this.step2Progress.set(0);
  }

  private openPopup(type: OperationType): void {
    this.operationType.set(type);
    this.step1Progress.set(0);
    this.step2Progress.set(0);
    this.showPopup.set(true);
    this.runMockProgress();
  }

  private runMockProgress(): void {
    this.stopAnimation();
    let phase = 1;
    const singleStep = !this.hasStep2();
    this.animationTimer = setInterval(() => {
      if (phase === 1) {
        const next = Math.min(this.step1Progress() + this.randomIncrement(), 100);
        this.step1Progress.set(next);
        if (next >= 100) {
          if (singleStep) { this.stopAnimation(); return; }
          phase = 2;
        }
      } else {
        const next = Math.min(this.step2Progress() + this.randomIncrement(), 100);
        this.step2Progress.set(next);
        if (next >= 100) this.stopAnimation();
      }
    }, 120);
  }

  private stopAnimation(): void {
    if (this.animationTimer) {
      clearInterval(this.animationTimer);
      this.animationTimer = null;
    }
  }

  private randomIncrement(): number {
    return Math.floor(Math.random() * 6) + 2;
  }
}
