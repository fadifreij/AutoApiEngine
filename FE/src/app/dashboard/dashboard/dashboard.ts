import { Component, inject, effect } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { WorkspaceStateService } from '../../shared/workspace-state.service';

interface WorkspaceStats {
  id: string;
  name: string;
  databaseName: string;
  databaseEngine: string;
  tablesCount: number;
  functionsCount: number;
  storedProceduresCount: number;
  databaseSizeBytes: number;
  lastSyncAt: string | null;
  isActive: boolean;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss'
})
export class Dashboard {
  private http = inject(HttpClient);
  private workspaceState = inject(WorkspaceStateService);

  stats: WorkspaceStats | null = null;
  loading = true;
  error = '';

  get workspaceName(): string {
    return this.stats?.name ?? (this.workspaceState.selectedWorkspaceName() || '...');
  }

  get lastSyncLabel(): string {
    if (!this.stats?.lastSyncAt) return 'Never';
    return this.timeAgo(this.stats.lastSyncAt);
  }

  get dbSizeLabel(): string {
    if (!this.stats?.databaseSizeBytes || this.stats.databaseSizeBytes === 0) return '0 B';
    const bytes = this.stats.databaseSizeBytes;
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    let i = 0;
    let size = bytes;
    while (size >= 1024 && i < units.length - 1) { size /= 1024; i++; }
    return `${size.toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
  }

  constructor() {
    effect(() => {
      const id = this.workspaceState.selectedWorkspaceId();
      if (id) this.fetchStats(id);
    });
  }

  private fetchStats(id: string): void {
    this.loading = true;
    this.error = '';
    this.http.get<WorkspaceStats>(`${environment.apiUrl}/workspaces/${id}/stats`).subscribe({
      next: (s) => { this.stats = s; this.loading = false; },
      error: () => { this.loading = false; this.error = 'Failed to load workspace stats.'; }
    });
  }

  private timeAgo(iso: string): string {
    const now = Date.now();
    const then = new Date(iso).getTime();
    const diff = Math.floor((now - then) / 1000);
    if (diff < 60) return 'Just now';
    if (diff < 3600) return `${Math.floor(diff / 60)} minutes ago`;
    if (diff < 86400) return `${Math.floor(diff / 3600)} hours ago`;
    if (diff < 2592000) return `${Math.floor(diff / 86400)} days ago`;
    return new Date(iso).toLocaleDateString();
  }
}