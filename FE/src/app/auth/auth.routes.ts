import { Routes } from '@angular/router';

export const authRoutes: Routes = [
  {
    path: '',
    redirectTo: 'signin',
    pathMatch: 'full',
  },
  {
    path: 'signup',
    loadComponent: () =>
      import('./pages/signup/signup.component').then((m) => m.SignupComponent),
    title: 'Create your account — Auto API Engine',
  },
  {
    path: 'signin',
    loadComponent: () =>
      import('./pages/signin/signin.component').then((m) => m.SigninComponent),
    title: 'Sign in — Auto API Engine',
  },
];
