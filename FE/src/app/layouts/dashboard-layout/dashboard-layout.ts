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

  constructor() {
    this.organizationName.set(this.authService.getOrganization());
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
