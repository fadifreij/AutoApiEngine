import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { Router } from '@angular/router';
import { TreeModule, TreeNodeSelectEvent } from 'primeng/tree';
import { CardModule } from 'primeng/card';
import { TableModule } from 'primeng/table';
import { PanelModule } from 'primeng/panel';
import { ButtonModule } from 'primeng/button';
import { TreeNode } from 'primeng/api';
import { WorkspaceStore, Workspace } from './store/workspace.store';

@Component({
  selector: 'app-workspace',
  imports: [TreeModule, CardModule, TableModule, PanelModule, ButtonModule],
  template: `
    <div class="ws-detail" [class.sidebar-collapsed]="!store.sidebarVisible()">
      <!-- Left sidebar: tree view -->
      @if (store.sidebarVisible()) {
        <aside class="ws-detail__sidebar" role="navigation" aria-label="Workspace tree">
          <div class="ws-detail__sidebar-header">
            <h2 class="ws-detail__sidebar-title">Workspaces</h2>
          </div>
          <p-tree
            [value]="store.treeNodes()"
            selectionMode="single"
            [(selection)]="selectedNode"
            (onNodeSelect)="onNodeSelect($event)"
            styleClass="ws-tree"
            [filter]="true"
            filterPlaceholder="Search..."
          />
        </aside>
      }

      <!-- Right content area -->
      <main class="ws-detail__content">
        @if (workspace(); as ws) {
          <div class="ws-detail__header">
            <p-button icon="pi pi-arrow-left" [text]="true" [rounded]="true" severity="secondary"
              (onClick)="goBack()" ariaLabel="Back to dashboard" />
            <div>
              <h1 class="ws-detail__name">
                <i class="pi pi-database"></i>
                {{ ws.name }}
              </h1>
              <span class="aae-badge" [class]="'aae-badge--' + ws.status">{{ ws.status }}</span>
            </div>
          </div>

          <!-- Info cards -->
          <div class="ws-detail__cards">
            <p-card>
              <ng-template #header>
                <div class="ws-detail__card-head">
                  <i class="pi pi-info-circle"></i> Database Info
                </div>
              </ng-template>
              <div class="ws-detail__info-grid">
                <div class="ws-detail__info-item">
                  <span class="ws-detail__info-label">Name</span>
                  <span class="ws-detail__info-value">{{ ws.name }}</span>
                </div>
                <div class="ws-detail__info-item">
                  <span class="ws-detail__info-label">Type</span>
                  <span class="ws-detail__info-value">{{ ws.dbType }}</span>
                </div>
                <div class="ws-detail__info-item">
                  <span class="ws-detail__info-label">Size</span>
                  <span class="ws-detail__info-value">{{ formatSize(ws.sizeBytes) }}</span>
                </div>
                <div class="ws-detail__info-item">
                  <span class="ws-detail__info-label">Status</span>
                  <span class="ws-detail__info-value">{{ ws.status }}</span>
                </div>
              </div>
            </p-card>

            <p-card>
              <ng-template #header>
                <div class="ws-detail__card-head">
                  <i class="pi pi-cog"></i> Actions
                </div>
              </ng-template>
              <div class="ws-detail__actions">
                <p-button label="Upload Database (.bak)" icon="pi pi-upload" severity="secondary" [outlined]="true" />
                <p-button label="Backup Database" icon="pi pi-download" severity="secondary" [outlined]="true" />
                <p-button label="Delete Workspace" icon="pi pi-trash" severity="danger" [outlined]="true" />
              </div>
            </p-card>
          </div>

          <!-- Placeholder table -->
          <p-panel header="Tables" [toggleable]="true" styleClass="ws-detail__panel">
            <p-table [value]="mockTables()" [tableStyle]="{ 'min-width': '50rem' }">
              <ng-template #header>
                <tr>
                  <th>Name</th>
                  <th>Rows</th>
                  <th>Size</th>
                  <th>Last Modified</th>
                </tr>
              </ng-template>
              <ng-template #body let-row>
                <tr>
                  <td>{{ row.name }}</td>
                  <td>{{ row.rows }}</td>
                  <td>{{ row.size }}</td>
                  <td>{{ row.modified }}</td>
                </tr>
              </ng-template>
            </p-table>
          </p-panel>

        } @else {
          <div class="ws-detail__empty">
            <i class="pi pi-database" style="font-size: 3rem; color: var(--aae-text-muted)"></i>
            <h2>Select a workspace</h2>
            <p>Choose a workspace from the tree to view its details.</p>
          </div>
        }
      </main>
    </div>
  `,
  styles: [`
    .ws-detail {
      display: flex;
      height: 100%;
      overflow: hidden;
    }

    .ws-detail__sidebar {
      width: var(--aae-sidebar-width, 260px);
      background: var(--aae-surface);
      border-right: 1px solid var(--aae-border);
      display: flex;
      flex-direction: column;
      overflow-y: auto;
      flex-shrink: 0;
    }

    .ws-detail__sidebar-header {
      padding: 1rem 1rem 0.5rem;
    }

    .ws-detail__sidebar-title {
      font-size: 0.85rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--aae-text-muted);
      margin: 0;
    }

    .ws-detail__content {
      flex: 1;
      overflow-y: auto;
      padding: 2rem;
    }

    .ws-detail__header {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      margin-bottom: 2rem;
    }

    .ws-detail__name {
      margin: 0;
      font-size: 1.5rem;
      display: flex;
      align-items: center;
      gap: 0.5rem;
      i { color: var(--aae-accent); }
    }

    .ws-detail__cards {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
      gap: 1.25rem;
      margin-bottom: 2rem;
    }

    .ws-detail__card-head {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      padding: 1rem 1.25rem;
      font-weight: 600;
      i { color: var(--aae-accent); }
    }

    .ws-detail__info-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 1rem;
    }

    .ws-detail__info-item {
      display: flex;
      flex-direction: column;
      gap: 0.2rem;
    }

    .ws-detail__info-label {
      font-size: 0.75rem;
      color: var(--aae-text-muted);
      text-transform: uppercase;
      letter-spacing: 0.04em;
    }

    .ws-detail__info-value {
      font-weight: 500;
    }

    .ws-detail__actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }

    .ws-detail__panel {
      margin-top: 1.5rem;
    }

    .ws-detail__empty {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      height: 100%;
      gap: 0.75rem;
      text-align: center;
      color: var(--aae-text-muted);
      h2 { margin: 0; color: var(--aae-text); }
    }

    .aae-badge--ready {
      background: rgba(34, 197, 94, 0.15);
      color: var(--aae-success);
      border: 1px solid var(--aae-success);
    }

    .aae-badge--error {
      background: rgba(239, 68, 68, 0.15);
      color: var(--aae-danger);
      border: 1px solid var(--aae-danger);
    }

    .aae-badge--empty {
      background: var(--aae-surface-2);
      color: var(--aae-text-muted);
      border: 1px solid var(--aae-border);
    }

    @media (max-width: 768px) {
      .ws-detail { flex-direction: column; }
      .ws-detail__sidebar { width: 100%; border-right: none; border-bottom: 1px solid var(--aae-border); max-height: 40vh; }
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkspaceComponent {
  protected readonly store = inject(WorkspaceStore);
  private readonly router = inject(Router);

  protected selectedNode = signal<TreeNode | null>(null);

  protected readonly workspace = computed(() => this.store.selected());

  protected readonly mockTables = computed(() => {
    const ws = this.workspace();
    if (!ws) return [];
    return [
      { name: 'Table_A', rows: '12,340', size: '24 MB', modified: 'Apr 5, 2026' },
      { name: 'Table_B', rows: '8,910', size: '16 MB', modified: 'Apr 3, 2026' },
      { name: 'Table_C', rows: '45,200', size: '102 MB', modified: 'Apr 1, 2026' },
    ];
  });

  onNodeSelect(event: TreeNodeSelectEvent): void {
    const node = event.node;
    if (node?.data) {
      this.store.selectWorkspace(node.data as Workspace);
    }
  }

  goBack(): void {
    this.router.navigate(['/dashboard']);
  }

  formatSize(bytes?: number): string {
    if (!bytes) return '—';
    const mb = bytes / (1024 * 1024);
    return mb >= 1024 ? `${(mb / 1024).toFixed(1)} GB` : `${mb.toFixed(0)} MB`;
  }
}
