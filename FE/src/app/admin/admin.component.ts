import { Component, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-admin',
  imports: [CommonModule],
  template: `
    <div class="admin-container">
      <h1>Admin</h1>
      <p>Admin panel - manage users and settings</p>
    </div>
  `,
  styles: [`
    .admin-container {
      padding: 2rem;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminComponent {}
