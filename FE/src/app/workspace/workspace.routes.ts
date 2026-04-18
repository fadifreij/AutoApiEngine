import { Routes } from '@angular/router';

export const workspaceRoutes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/main-workspace/main-workspace.component').then(
        (m) => m.MainWorkspaceComponent
      ),
  },
  {
    path: 'create',
    loadComponent: () =>
      import('./components/create-workspace/create-workspace.component').then(
        (m) => m.CreateWorkspaceComponent
      ),
  },
  {
    path: 'workspace/:id',
    loadComponent: () =>
      import('./workspace.component').then((m) => m.WorkspaceComponent),
  },
];
