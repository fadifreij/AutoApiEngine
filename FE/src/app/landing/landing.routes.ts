import { Routes } from '@angular/router';
import { guestGuard } from '../core/guards/guest.guard';

export const landingRoutes: Routes = [
  {
    path: '',
    canActivate: [guestGuard],
    loadComponent: () =>
      import('./landing.component').then((m) => m.LandingComponent),
    title: 'Auto API Engine — API & Database Platform',
  },
];