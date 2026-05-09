import { Component, HostListener, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../shared/auth/auth.service';

@Component({
  selector: 'app-dashboard-layout',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './dashboard-layout.html',
  styleUrl: './dashboard-layout.scss'
})
export class DashboardLayout {
  private authService = inject(AuthService);
  profileOpen = signal(false);
  organizationName = signal<string | null>(null);
  userEmail = signal<string>('');
  userName = signal<string>('');

  constructor() {
    this.organizationName.set(this.authService.getOrganization());
    this.loadUserInfo();
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
    this.profileOpen.set(!this.profileOpen());
  }

  @HostListener('document:click')
  onDocClick() {
    this.profileOpen.set(false);
  }

  signOut(event: MouseEvent) {
    event.stopPropagation();
    this.authService.logout();
  }
}