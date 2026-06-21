import { Routes } from '@angular/router';
import { authGuard, publicGuard } from './shared/auth/auth.guard';

export const routes: Routes = [
  { path: '', canActivate: [publicGuard], loadComponent: () => import('./pages/landing/landing').then(m => m.Landing) },
  { path: 'login', canActivate: [publicGuard], loadComponent: () => import('./pages/login/login').then(m => m.Login) },
  { path: 'register', canActivate: [publicGuard], loadComponent: () => import('./pages/register/register').then(m => m.Register) },
  { path: 'auth/callback', loadComponent: () => import('./pages/auth-callback/auth-callback').then(m => m.AuthCallback) },
  { path: 'query-studio', canActivate: [authGuard], loadComponent: () => import('./dashboard/query-studio/query-studio').then(m => m.QueryStudio) },
  { path: 'pricing', loadComponent: () => import('./pages/pricing/pricing').then(m => m.Pricing) },
  { path: 'docs', loadComponent: () => import('./pages/docs/docs').then(m => m.Docs) },
  {
    path: 'app',
    canActivate: [authGuard],
    loadComponent: () => import('./layouts/dashboard-layout/dashboard-layout').then(m => m.DashboardLayout),
    children: [
      { path: 'dashboard', loadComponent: () => import('./dashboard/dashboard/dashboard').then(m => m.Dashboard) },
      { path: 'workspace/new', loadComponent: () => import('./dashboard/workspace-new/workspace-new').then(m => m.WorkspaceNew) },
      { path: 'workspace/manage', loadComponent: () => import('./dashboard/workspace-manage/workspace-manage').then(m => m.WorkspaceManage) },
      { path: 'apis/generated', loadComponent: () => import('./dashboard/api-generated/api-generated').then(m => m.ApiGenerated) },
      { path: 'apis/custom', loadComponent: () => import('./dashboard/api-custom/api-custom').then(m => m.ApiCustom) },
      { path: 'query-studio', redirectTo: '/query-studio', pathMatch: 'full' },
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
    ]
  },
];
