import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';

/**
 * Not Found Component — 404 page displayed for unmatched routes.
 */
@Component({
  selector: 'app-not-found',
  imports: [RouterLink, ButtonModule],
  template: `
    <div class="not-found-container">
      <div class="content">
        <h1>404</h1>
        <h2>Page Not Found</h2>
        <p>The page you're looking for doesn't exist or has been moved.</p>
        <p-button
          [routerLink]="['/dashboard']"
          label="Back to Dashboard"
          icon="pi pi-home"
          severity="primary"
        ></p-button>
      </div>
    </div>
  `,
  styles: [`
    .not-found-container {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 100vh;
      background: var(--surface-ground);
    }

    .content {
      text-align: center;
      padding: 2rem;
    }

    h1 {
      font-size: 6rem;
      margin: 0;
      color: var(--primary-color);
    }

    h2 {
      font-size: 1.5rem;
      color: var(--text-color);
      margin-top: 1rem;
    }

    p {
      color: var(--text-color-secondary);
      margin-bottom: 1.5rem;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotFoundComponent {}
