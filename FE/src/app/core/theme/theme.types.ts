export type ThemeName = 'dark' | 'light' | 'ocean' | 'forest' | 'midnight';

export interface ThemeConfig {
  name: ThemeName;
  label: string;
  icon: string;          // PrimeIcons class
  primeNgTheme: string;  // path in /assets/themes/
  isDark: boolean;
}

export const THEMES: ThemeConfig[] = [
  {
    name: 'dark',
    label: 'Dark',
    icon: 'pi-moon',
    primeNgTheme: 'dark',
    isDark: true,
  },
  {
    name: 'light',
    label: 'Light',
    icon: 'pi-sun',
    primeNgTheme: 'light',
    isDark: false,
  },
  {
    name: 'ocean',
    label: 'Ocean',
    icon: 'pi-globe',
    primeNgTheme: 'ocean',
    isDark: true,
  },
  {
    name: 'forest',
    label: 'Forest',
    icon: 'pi-chart-line',
    primeNgTheme: 'forest',
    isDark: true,
  },
  {
    name: 'midnight',
    label: 'Midnight',
    icon: 'pi-star',
    primeNgTheme: 'midnight',
    isDark: true,
  },
];
