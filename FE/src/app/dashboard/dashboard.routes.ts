import { Routes } from '@angular/router';

export const dashboardRoutes: Routes = [
  {
    path: '',
    redirectTo: '/workspaces/dashboard',
    pathMatch: 'full',
  },
];
