import {
  ApplicationConfig,
  isDevMode,
} from '@angular/core';
import { provideRouter, withViewTransitions, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideClientHydration, withEventReplay } from '@angular/platform-browser';
import { providePrimeNG } from 'primeng/config';
import Aura from '@primeuix/themes/aura';

import { routes } from './app.routes';
import { jwtInterceptor } from './core/interceptors/jwt.interceptor';
import { errorInterceptor } from './core/interceptors/error.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [

    // Router — view transitions for smooth page changes, input binding for route params
    provideRouter(
      routes,
      withViewTransitions(),
      withComponentInputBinding()
    ),

    // HTTP — use fetch API (SSR-compatible), attach JWT and handle errors globally
    provideHttpClient(
      withFetch(),
      withInterceptors([jwtInterceptor, errorInterceptor])
    ),

    // Animations — async to keep initial bundle small
    provideAnimationsAsync(),

    // SSR hydration with event replay (Angular 18+)
    provideClientHydration(withEventReplay()),

    // PrimeNG theme — Aura preset with dark mode defaults
    providePrimeNG({
      theme: {
        preset: Aura,
        options: {
          darkModeSelector: '[data-theme="dark"],[data-theme="ocean"],[data-theme="forest"],[data-theme="midnight"]',
        },
      },
    }),
  ],
};



