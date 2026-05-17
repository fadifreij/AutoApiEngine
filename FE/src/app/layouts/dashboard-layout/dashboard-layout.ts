import { isPlatformBrowser } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, HostListener, inject, PLATFORM_ID, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../shared/auth/auth.service';
import { environment } from '../../../environments/environment';

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
  private authService = inject(AuthService);
  private http = inject(HttpClient);
  private platformId = inject(PLATFORM_ID);

  profileOpen = signal(false);
  organizationName = signal<string | null>(null);
  userEmail = signal<string>('');
  userName = signal<string>('');
  workspaces = signal<WorkspaceListItem[]>([]);
  selectedWorkspaceId = signal<string>('');
  workspacesLoading = signal(false);
  workspaceDropdownOpen = signal(false);

  constructor() {
    this.organizationName.set(this.authService.getOrganization());
    this.loadUserInfo();
    this.loadWorkspaces();
  }

  private loadUserInfo() {
    const token = this.authService.getAccessToken();
    if (token) {
      try {
        const payload = JSON.parse(atob(token.split('.')[1]));
        this.userEmail.set(payload.email || '');
        const givenName = payload.given_name || '';
        const familyName = payload.family_name || '';
        const name = givenName || familyName || payload.preferred_username || payload.email?.split('@')[0] || 'User';
        this.userName.set(name);
      } catch {
        this.userName.set('User');
      }
    }
  }

  private loadWorkspaces(): void {
    if (!isPlatformBrowser(this.platformId)) return;

    this.workspacesLoading.set(true);

    this.http.get<WorkspaceListItem[]>(`${environment.apiUrl}/workspaces/current-organization`).subscribe({
      next: (workspaces) => {
        this.workspaces.set(workspaces);
        this.workspacesLoading.set(false);

        const currentId = this.selectedWorkspaceId();
        if (!currentId || !workspaces.some(workspace => workspace.id === currentId)) {
          this.selectedWorkspaceId.set(workspaces[0]?.id ?? '');
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

    const selected = this.workspaces().find(workspace => workspace.id === this.selectedWorkspaceId());
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
    this.workspaceDropdownOpen.set(false);
  }

  getInitials(): string {
    const name = this.userName();
    if (!name || name === 'User') return 'U';
    const parts = name.trim().split(' ');
    if (parts.length >= 2) {
      return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
    }
    return name.substring(0, 2).toUpperCase();
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

  signOut(event: MouseEvent) {
    event.stopPropagation();
    this.authService.logout();
  }
}