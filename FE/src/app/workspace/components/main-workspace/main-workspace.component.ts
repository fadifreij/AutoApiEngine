import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { UpperCasePipe } from '@angular/common';
import { Workspace, WorkspaceStore } from '../../store/workspace.store';
import { url_workspace_id } from '../../../shared/constants';

@Component({
    selector: 'main-workspace',
    imports: [ButtonModule, UpperCasePipe],
    templateUrl: './main-workspace.component.html',
    styleUrl: './main-workspace.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MainWorkspaceComponent {
    protected readonly store = inject(WorkspaceStore);
    private readonly router = inject(Router);

    navigateToCreate(): void {
        this.router.navigate(['/workspaces/create']);
    }

    openWorkspace(ws: Workspace): void {
        this.router.navigate([url_workspace_id(ws.id)]);
    }

    formatSize(bytes?: number): string {
        if (!bytes) return '—';
        const mb = bytes / (1024 * 1024);
        return mb >= 1024 ? `${(mb / 1024).toFixed(1)} GB` : `${mb.toFixed(0)} MB`;
    }
}