import { isPlatformBrowser } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, HostListener, inject, PLATFORM_ID, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs/operators';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../shared/auth/auth.service';
import { WorkspaceStateService } from '../../shared/workspace-state.service';

interface WorkspaceListItem {
  id: string;
  name: string;
  databaseEngine: string;
  isActive: boolean;
}

@Component({
  selector: 'app-dashboard-layout',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './dashboard-layout.html',
  styleUrl: './dashboard-layout.scss'
})
export class DashboardLayout {
  authService = inject(AuthService);
  private http = inject(HttpClient);
  private platformId = inject(PLATFORM_ID);
  private workspaceState = inject(WorkspaceStateService);
  private router = inject(Router);

  profileOpen = signal(false);
  organizationName = signal<string | null>(null);
  workspaces = signal<WorkspaceListItem[]>([]);
  selectedWorkspaceId = signal<string>('');
  workspacesLoading = signal(false);
  workspaceDropdownOpen = signal(false);

  constructor() {
    this.organizationName.set(this.authService.getOrganization());
    this.loadWorkspaces();

    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd)
    ).subscribe(() => {
      this.loadWorkspaces();
    });
  }

  private loadWorkspaces(): void {
    if (!isPlatformBrowser(this.platformId)) return;

    this.workspacesLoading.set(true);

    this.http.get<WorkspaceListItem[]>(`${environment.apiUrl}/workspaces/current-organization`).subscribe({
      next: (workspaces) => {
        this.workspaces.set(workspaces);
        this.workspacesLoading.set(false);

        const stateId = this.workspaceState.selectedWorkspaceId();
        if (stateId && workspaces.some(w => w.id === stateId)) {
          this.selectedWorkspaceId.set(stateId);
        } else {
          const currentId = this.selectedWorkspaceId();
          if (!currentId || !workspaces.some(w => w.id === currentId)) {
            this.selectedWorkspaceId.set(workspaces[0]?.id ?? '');
          }
        }
        this.syncWorkspaceState();

        // On initial load, also switch MCP to the default workspace
        const activeId = this.workspaceState.selectedWorkspaceId();
        if (activeId) {
          this.switchMcp(activeId);
        }
      },
      error: () => {
        this.workspaces.set([]);
        this.selectedWorkspaceId.set('');
        this.workspacesLoading.set(false);
      }
    });
  }

  selectedWorkspaceName(): string {
    if (this.workspacesLoading()) return 'Loading workspaces...';
    const selected = this.workspaces().find(w => w.id === this.selectedWorkspaceId());
    return selected?.name ?? 'No workspaces';
  }

  toggleWorkspaceDropdown(event: MouseEvent): void {
    event.stopPropagation();
    if (this.workspacesLoading() || this.workspaces().length === 0) return;
    this.profileOpen.set(false);
    this.workspaceDropdownOpen.set(!this.workspaceDropdownOpen());
  }

  selectWorkspace(workspaceId: string, event: MouseEvent): void {
    event.stopPropagation();
    this.selectedWorkspaceId.set(workspaceId);
    const name = this.workspaces().find(w => w.id === workspaceId)?.name ?? '';
    this.workspaceState.setSelectedWorkspace(workspaceId, name);
    this.workspaceDropdownOpen.set(false);

    // Notify backend to switch MCP config to this workspace's database
    this.switchMcp(workspaceId);
  }

  /**
   * Calls POST /api/workspaces/{id}/switch-mcp to rebuild the MCP config
   * and restart the OpenCode server with the new workspace's database connection.
   */
  private switchMcp(workspaceId: string): void {
    this.http.post(`${environment.apiUrl}/workspaces/${workspaceId}/switch-mcp`, {}).subscribe({
      next: (res: any) => {
        console.log('[MCP] Switched:', res?.message);
      },
      error: (err) => {
        console.warn('[MCP] Switch failed (non-blocking):', err?.error?.message || err.message);
      }
    });
  }

  toggleProfile(event: MouseEvent) {
    event.stopPropagation();
    this.workspaceDropdownOpen.set(false);
    this.profileOpen.set(!this.profileOpen());
  }

  @HostListener('document:click')
  onDocClick() {
    this.profileOpen.set(false);
    this.workspaceDropdownOpen.set(false);
  }

  private syncWorkspaceState(): void {
    const id = this.selectedWorkspaceId();
    const name = this.selectedWorkspaceName();
    this.workspaceState.setSelectedWorkspace(id, name);
  }

  signOut(event: MouseEvent) {
    event.stopPropagation();
    this.authService.logout();
  }
}