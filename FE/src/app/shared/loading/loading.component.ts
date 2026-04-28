import { Component, inject } from '@angular/core';
import { LoadingService } from './loading.service';

@Component({
  selector: 'app-loading',
  standalone: true,
  template: `
    @if (loading.visible()) {
      <div class="loading-overlay">
        <div class="loading-spinner">
          <div class="spinner-ring"></div>
          <div class="spinner-ring"></div>
          <div class="spinner-ring"></div>
        </div>
      </div>
    }
  `,
  styles: [`
    .loading-overlay {
      position: fixed;
      inset: 0;
      z-index: 9999;
      display: flex;
      align-items: center;
      justify-content: center;
      background: rgba(255, 255, 255, 0.85);
      backdrop-filter: blur(4px);
      animation: fadeIn 0.15s ease-out;
    }

    @keyframes fadeIn {
      from { opacity: 0; }
      to { opacity: 1; }
    }

    .loading-spinner {
      position: relative;
      width: 48px;
      height: 48px;
    }

    .spinner-ring {
      position: absolute;
      inset: 0;
      border-radius: 50%;
      border: 3px solid transparent;
      border-top-color: var(--primary, #4f46e5);
      animation: spin 1s cubic-bezier(0.68, -0.55, 0.27, 1.55) infinite;
    }

    .spinner-ring:nth-child(2) {
      inset: 6px;
      border-top-color: var(--primary-light, #818cf8);
      animation-delay: 0.15s;
      animation-direction: reverse;
    }

    .spinner-ring:nth-child(3) {
      inset: 12px;
      border-top-color: var(--accent, #7c3aed);
      animation-delay: 0.3s;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }
  `]
})
export class LoadingComponent {
  protected loading = inject(LoadingService);
}
