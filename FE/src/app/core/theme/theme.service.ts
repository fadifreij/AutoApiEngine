import {
  Injectable,
  signal,
  computed,
  effect,
  inject,
  PLATFORM_ID,
} from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { THEMES, ThemeName, ThemeConfig } from './theme.types';

const STORAGE_KEY = 'aae_theme';
const DEFAULT_THEME: ThemeName = 'dark';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly platformId = inject(PLATFORM_ID);
  private readonly isBrowser = isPlatformBrowser(this.platformId);

  // ── Core signal ────────────────────────────────────────────────────────────
  readonly active = signal<ThemeName>(this.resolveInitialTheme());

  // ── Derived signals ────────────────────────────────────────────────────────
  readonly config = computed<ThemeConfig>(
    () => THEMES.find((t) => t.name === this.active())!
  );
  readonly isDark = computed(() => this.config().isDark);

  constructor() {
    // Apply theme whenever signal changes — browser only
    effect(() => {
      const theme = this.active();
      if (this.isBrowser) {
        this.applyToDom(theme);
      }
    });
  }

  // ── Public API ─────────────────────────────────────────────────────────────
  apply(theme: ThemeName): void {
    this.active.set(theme);
    if (this.isBrowser) {
      localStorage.setItem(STORAGE_KEY, theme);
    }
  }

  toggle(): void {
    this.apply(this.isDark() ? 'light' : 'dark');
  }

  allThemes(): ThemeConfig[] {
    return THEMES;
  }

  // ── Private helpers ────────────────────────────────────────────────────────
  private resolveInitialTheme(): ThemeName {
    if (!this.isBrowser) return DEFAULT_THEME;

    const stored = localStorage.getItem(STORAGE_KEY) as ThemeName | null;
    if (stored && THEMES.find((t) => t.name === stored)) return stored;

    // Respect OS preference on first visit
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    return prefersDark ? 'dark' : 'light';
  }

  private applyToDom(theme: ThemeName): void {
    // Swap PrimeNG theme link element
    const linkEl = document.getElementById('app-theme') as HTMLLinkElement | null;
    if (linkEl) {
      linkEl.href = `/assets/themes/${theme}/theme.css`;
    }

    // Set data-theme attribute on <html> for custom CSS variable overrides
    document.documentElement.setAttribute('data-theme', theme);

    // Toggle dark class for Tailwind-compatible dark mode (if ever needed)
    const config = THEMES.find((t) => t.name === theme)!;
    document.documentElement.classList.toggle('dark', config.isDark);
  }
}
