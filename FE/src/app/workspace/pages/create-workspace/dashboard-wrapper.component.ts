import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TreeNode } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { TreeModule } from 'primeng/tree';
import { Workspace, WorkspaceStore } from '../../store/workspace.store';

@Component({
    selector: 'app-dashboard-wrapper',
    imports: [ButtonModule, TreeModule],
    template: `
    <div class="dash-wrapper" [class.sidebar-collapsed]="!store.sidebarVisible()">
      @if (store.sidebarVisible()) {
        <aside class="dash-wrapper__sidebar" role="navigation" aria-label="Workspace tree">
          <div class="dash-wrapper__sidebar-header">
            <h2 class="dash-wrapper__sidebar-title">Workspaces List</h2>
            <p-button
              icon="pi pi-plus"
              [rounded]="true"
              [text]="true"
              severity="success"
              (onClick)="navigateToCreate()"
              ariaLabel="Add new workspace"
              size="small"
            />
          </div>
          <p class="dash-wrapper__sidebar-help">Select a workspace to open details.</p>
          <p-tree
            [value]="store.treeNodes()"
            selectionMode="single"
            [(selection)]="selectedNode"
            (onNodeSelect)="onNodeSelect($event)"
            styleClass="ws-tree"
          />
        </aside>
      }

      <main class="dash-wrapper__content">
        <div class="dash-wrapper__hero aae-card">
          <h1>Welcome to Workspaces Management</h1>
          <p>
            Use this page to onboard databases, review backups, and navigate tables,
            stored procedures, and functions from the left workspace tree.
          </p>
          <div class="dash-wrapper__hero-actions">
            <p-button
              label="Add New Workspace"
              icon="pi pi-plus"
              (onClick)="navigateToCreate()"
            />
            <p-button
              label="Open First Workspace"
              icon="pi pi-arrow-right"
              severity="secondary"
              [outlined]="true"
              [disabled]="!store.workspaces().length"
              (onClick)="openWorkspace(store.workspaces()[0])"
            />
          </div>
        </div>

        <div class="dash-wrapper__grid">
          <section class="aae-card dash-wrapper__guide-card" aria-labelledby="how-to-title">
            <h2 id="how-to-title">How To Use</h2>
            <ul>
              <li>Click a workspace from the left panel to open detailed view.</li>
              <li>Expand tree nodes to inspect Tables, Backups, Stored Procedures, and Functions.</li>
              <li>Use Add New Workspace to register a database and save generated API key credentials.</li>
            </ul>
          </section>

          <section class="aae-card dash-wrapper__guide-card" aria-labelledby="summary-title">
            <h2 id="summary-title">Workspace Summary</h2>
            <div class="dash-wrapper__summary-row">
              <span>Total Workspaces</span>
              <strong>{{ store.workspaceCount() }}</strong>
            </div>
            <div class="dash-wrapper__summary-row">
              <span>Ready Status</span>
              <strong>{{ store.workspaces().filter((w) => w.status === 'ready').length }}</strong>
            </div>
            <div class="dash-wrapper__summary-row">
              <span>Needs Setup</span>
              <strong>{{ store.workspaces().filter((w) => w.status === 'empty').length }}</strong>
            </div>
          </section>

          <section class="aae-card dash-wrapper__guide-card" aria-labelledby="quick-start-title">
            <h2 id="quick-start-title">Quick Start</h2>
            <div class="dash-wrapper__quick-grid">
              <button type="button" class="dash-wrapper__quick-card" (click)="navigateToCreate()">
                <i class="pi pi-plus-circle"></i>
                <span>Create workspace</span>
              </button>
              <button type="button" class="dash-wrapper__quick-card" [disabled]="!store.workspaces().length" (click)="openWorkspace(store.workspaces()[0])">
                <i class="pi pi-folder-open"></i>
                <span>Open workspace</span>
              </button>
            </div>
          </section>

          <section class="aae-card dash-wrapper__guide-card" aria-labelledby="onboarding-title">
            <h2 id="onboarding-title">Onboarding Checklist</h2>
            <div class="dash-wrapper__check-item">
              <i class="pi" [class.pi-check-circle]="store.workspaceCount() > 0" [class.pi-circle]="store.workspaceCount() === 0"></i>
              <span>Create your first workspace</span>
            </div>
            <div class="dash-wrapper__check-item">
              <i class="pi" [class.pi-check-circle]="store.workspaces().filter((w) => w.status === 'ready').length > 0" [class.pi-circle]="store.workspaces().filter((w) => w.status === 'ready').length === 0"></i>
              <span>Mark at least one workspace as ready</span>
            </div>
            <div class="dash-wrapper__check-item">
              <i class="pi pi-circle"></i>
              <span>Review backups, procedures, and functions in left tree</span>
            </div>
          </section>
        </div>
      </main>
    </div>
  `,
    styles: [`
    .dash-wrapper {
      display: flex;
      height: 100%;
      overflow: hidden;
    }

    .dash-wrapper__sidebar {
      width: var(--aae-sidebar-width, 280px);
      background: var(--aae-surface);
      border-right: 1px solid var(--aae-border);
      display: flex;
      flex-direction: column;
      overflow-y: auto;
      flex-shrink: 0;
    }

    .dash-wrapper__sidebar-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 1rem 1rem 0.5rem;
    }

    .dash-wrapper__sidebar-title {
      font-size: 0.85rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--aae-text-muted);
      margin: 0;
    }

    .dash-wrapper__sidebar-help {
      margin: 0;
      padding: 0 1rem 0.85rem;
      font-size: 0.8rem;
      color: var(--aae-text-muted);
    }

    .dash-wrapper__content {
      flex: 1;
      overflow-y: auto;
      padding: 2rem;
      display: flex;
      flex-direction: column;
      gap: 1.25rem;
    }

    .dash-wrapper__hero {
      padding: 1.5rem;
      background:
        radial-gradient(circle at top right, rgba(59, 130, 246, 0.2), transparent 55%),
        radial-gradient(circle at bottom left, rgba(16, 185, 129, 0.2), transparent 45%),
        var(--aae-surface);

      h1 {
        margin: 0 0 0.4rem;
        font-size: 1.6rem;
      }

      p {
        margin: 0;
        color: var(--aae-text-muted);
        max-width: 65ch;
        line-height: 1.6;
      }
    }

    .dash-wrapper__hero-actions {
      margin-top: 1rem;
      display: flex;
      gap: 0.65rem;
      flex-wrap: wrap;
    }

    .dash-wrapper__grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(290px, 1fr));
      gap: 1.25rem;
    }

    .dash-wrapper__guide-card {
      padding: 1.25rem;

      h2 {
        margin: 0 0 0.75rem;
        font-size: 1.1rem;
      }

      ul {
        margin: 0;
        padding-left: 1.2rem;
        color: var(--aae-text-muted);
        display: grid;
        gap: 0.45rem;
      }
    }

    .dash-wrapper__summary-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 0.55rem 0;
      border-bottom: 1px dashed var(--aae-border);

      span {
        color: var(--aae-text-muted);
      }

      strong {
        font-size: 1.05rem;
      }
    }

    .dash-wrapper__quick-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 0.65rem;
    }

    .dash-wrapper__quick-card {
      border: 1px solid var(--aae-border);
      background: var(--aae-surface);
      border-radius: 12px;
      padding: 0.8rem;
      display: flex;
      align-items: center;
      gap: 0.5rem;
      color: var(--aae-text);
      cursor: pointer;
      transition: border-color 120ms ease, transform 120ms ease;

      i {
        color: var(--aae-accent);
      }

      &:hover:not(:disabled) {
        border-color: var(--aae-accent);
        transform: translateY(-1px);
      }

      &:disabled {
        opacity: 0.6;
        cursor: not-allowed;
      }
    }

    .dash-wrapper__check-item {
      display: flex;
      align-items: center;
      gap: 0.55rem;
      padding: 0.4rem 0;

      i {
        color: var(--aae-accent);
      }
    }

    @media (max-width: 768px) {
      .dash-wrapper {
        flex-direction: column;
      }

      .dash-wrapper__sidebar {
        width: 100%;
        border-right: none;
        border-bottom: 1px solid var(--aae-border);
        max-height: 40vh;
      }
    }
  `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardWrapperComponent {
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
        this.router.navigate(['/workspaces']);
    }

    onNodeSelect(event: { node?: TreeNode }): void {
        const node = event.node;
        if (node?.data) {
            this.store.selectWorkspace(node.data as Workspace);
            this.router.navigate(['/workspaces']);
        }
    }
}
