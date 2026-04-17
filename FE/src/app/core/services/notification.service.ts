import { Injectable, signal, computed } from '@angular/core';
import { MessageService } from 'primeng/api';

export interface Toast {
  id: string;
  severity: 'success' | 'info' | 'warn' | 'error';
  summary: string;
  detail?: string;
}

@Injectable({ providedIn: 'root' })
export class NotificationService {
  // Injected at app level — requires <p-toast> in AppComponent
  constructor(private messageService: MessageService) {}

  success(summary: string, detail?: string): void {
    this.messageService.add({ severity: 'success', summary, detail, life: 4000 });
  }

  info(summary: string, detail?: string): void {
    this.messageService.add({ severity: 'info', summary, detail, life: 5000 });
  }

  warn(summary: string, detail?: string): void {
    this.messageService.add({ severity: 'warn', summary, detail, life: 6000 });
  }

  error(summary: string, detail?: string): void {
    this.messageService.add({ severity: 'error', summary, detail, life: 8000, sticky: false });
  }

  clear(): void {
    this.messageService.clear();
  }
}
