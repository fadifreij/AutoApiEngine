import { Component, inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { TooltipModule } from 'primeng/tooltip';
import { ThemeService } from '../theme.service';
import { THEMES } from '../theme.types';

@Component({
  selector: 'app-theme-switcher',
  standalone: true,
  imports: [CommonModule, ButtonModule, TooltipModule],
  template: `
    <div class="theme-switcher">
      <button
        pButton
        type="button"
        class="p-button-text p-button-rounded"
        [icon]="'pi ' + themeService.config().icon"
        [pTooltip]="'Theme: ' + themeService.config().label"
        tooltipPosition="bottom"
        (click)="toggleThemes()"
        aria-label="Switch theme"
      ></button>

      @if (showThemes) {
        <div class="theme-menu">
          <div class="theme-menu-header">Choose theme</div>
          @for (theme of themes; track theme.name) {
            <button
              type="button"
              class="theme-option"
              [class.active]="themeService.active() === theme.name"
              (click)="select(theme)"
              [attr.aria-pressed]="themeService.active() === theme.name"
            >
              <span class="theme-swatch" [style.background-color]="theme.primeNgTheme"></span>
              <span class="theme-label">{{ theme.label }}</span>
              @if (themeService.active() === theme.name) {
                <i class="pi pi-check theme-check"></i>
              }
            </button>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    :host {
      display: inline-block;
    }

    .theme-switcher {
      position: relative;
      display: inline-block;
    }

    .theme-menu {
      position: absolute;
      top: 100%;
      right: 0;
      background: var(--surface-card);
      border: 1px solid var(--surface-border);
      border-radius: 6px;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
      min-width: 160px;
      z-index: 1000;
      margin-top: 0.5rem;
    }

    .theme-menu-header {
      font-size: 0.7rem;
      font-weight: 600;
      letter-spacing: 0.08em;
      text-transform: uppercase;
      color: var(--text-color-secondary);
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--surface-border);
    }

    .theme-option {
      display: flex;
      align-items: center;
      gap: 0.65rem;
      padding: 0.5rem 0.75rem;
      border: none;
      background: transparent;
      cursor: pointer;
      width: 100%;
      text-align: left;
      color: var(--text-color);
      transition: background 0.15s;
      font-size: 0.875rem;

      &:hover {
        background: var(--surface-hover);
      }

      &.active {
        background: var(--primary-color-transparent, color-mix(in srgb, var(--primary-color) 15%, transparent));
        color: var(--primary-color);
      }
    }

    .theme-swatch {
      width: 16px;
      height: 16px;
      border-radius: 50%;
      flex-shrink: 0;
      border: 1px solid var(--surface-border);
    }

    .theme-check {
      margin-left: auto;
      font-size: 0.75rem;
    }

    .theme-label {
      flex: 1;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ThemeSwitcherComponent {
  readonly themeService = inject(ThemeService);
  readonly themes = THEMES;
  showThemes = false;

  toggleThemes(): void {
    this.showThemes = !this.showThemes;
  }

  select(theme: typeof THEMES[0]): void {
    this.themeService.apply(theme.name);
    this.showThemes = false;
  }
}

