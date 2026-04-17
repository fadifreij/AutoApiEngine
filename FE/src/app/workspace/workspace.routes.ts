import { Routes } from '@angular/router';

export const workspaceRoutes: Routes = [
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./pages/create-workspace/dashboard-wrapper.component').then(
        (m) => m.DashboardWrapperComponent
      ),
  },
  {
    path: 'create',
    loadComponent: () =>
      import('./pages/create-workspace/create-workspace.component').then(
        (m) => m.CreateWorkspaceComponent
      ),
  },
  {
    path: '',
    loadComponent: () =>
      import('./workspace.component').then((m) => m.WorkspaceComponent),
  },
];
