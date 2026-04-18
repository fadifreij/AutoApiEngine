import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TreeNode } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { TreeModule } from 'primeng/tree';
import { Workspace, WorkspaceStore } from '../../store/workspace.store';
import { url_workspace } from '../../../shared/constants';

@Component({
    selector: 'main-workspace',
    imports: [ButtonModule, TreeModule],
    templateUrl: './main-workspace.component.html',
    styleUrl: './main-workspace.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MainWorkspaceComponent {
    protected readonly store = inject(WorkspaceStore);
    private readonly router = inject(Router);

    protected readonly selectedNode = signal<TreeNode | null>(null);

    navigateToCreate(): void {
        this.router.navigate(['/workspaces/create']);
    }

    openWorkspace(ws?: Workspace): void {
        if (!ws) {
            return;
        }

        this.store.selectWorkspace(ws);
        this.router.navigate([url_workspace]);
    }

    onNodeSelect(event: { node?: TreeNode }): void {
        const node = event.node;
        if (node?.data) {
            this.store.selectWorkspace(node.data as Workspace);
            this.router.navigate(['/workspaces']);
        }
    }
}
