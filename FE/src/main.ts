import { bootstrapApplication } from '@angular/platform-browser';
import { provideZonelessChangeDetection } from '@angular/core';
import { MessageService } from 'primeng/api';
import { appConfig } from './app/app.config';
import { App } from './app/app';

bootstrapApplication(App, {
  ...appConfig,
  providers: [
    provideZonelessChangeDetection(),
   MessageService,
    ...appConfig.providers,
  ],
})
  .catch((err) => console.error(err));
