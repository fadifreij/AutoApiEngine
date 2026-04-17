import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { PanelMenuModule } from 'primeng/panelmenu';
import { Workspace, WorkspaceStore } from '../workspace/store/workspace.store';

@Component({
  selector: 'app-dashboard',
  imports: [CardModule, ButtonModule, PanelMenuModule],
  template: `
    <div class="dashboard" [class.sidebar-collapsed]="!store.sidebarVisible()">
      <!-- Left sidebar -->
      @if (store.sidebarVisible()) {
        <aside class="dashboard__sidebar" role="navigation" aria-label="Workspace navigation">
          <div class="dashboard__sidebar-header">
            <h2 class="dashboard__sidebar-title">Workspaces</h2>
            <p-button
              icon="pi pi-plus"
              [rounded]="true"
              [text]="true"
              severity="success"
              (onClick)="addWorkspace()"
              ariaLabel="Add new workspace"
              size="small"
            />
          </div>
          <p-panelMenu [model]="store.panelMenuItems()" styleClass="workspace-panel-menu" />
        </aside>
      }

      <!-- Right content -->
      <main class="dashboard__content">
        <div class="dashboard__header">
          <h1>Workspaces Dashboard</h1>
          <p class="dashboard__subtitle">Manage your database workspaces</p>
        </div>

        <div class="dashboard__stats">
          <div class="aae-card dashboard__stat-card">
            <div class="dashboard__stat-icon"><i class="pi pi-database"></i></div>
            <div>
              <div class="dashboard__stat-value">{{ store.workspaceCount() }}</div>
              <div class="dashboard__stat-label">Total Workspaces</div>
            </div>
          </div>
          <div class="aae-card dashboard__stat-card">
            <div class="dashboard__stat-icon dashboard__stat-icon--success"><i class="pi pi-check-circle"></i></div>
            <div>
              <div class="dashboard__stat-value">{{ readyCount() }}</div>
              <div class="dashboard__stat-label">Ready</div>
            </div>
          </div>
        </div>

        <div class="dashboard__grid">
          @for (ws of store.workspaces(); track ws.id) {
            <p-card styleClass="dashboard__workspace-card">
              <ng-template #header>
                <div class="dashboard__card-header">
                  <i class="pi pi-database"></i>
                  <span>{{ ws.name }}</span>
                  <span class="aae-badge" [class]="'aae-badge--' + ws.status">{{ ws.status }}</span>
                </div>
              </ng-template>
              <div class="dashboard__card-body">
                <div class="dashboard__card-row">
                  <span class="dashboard__card-label">Type</span>
                  <span class="dashboard__card-value">{{ ws.dbType }}</span>
                </div>
                <div class="dashboard__card-row">
                  <span class="dashboard__card-label">Size</span>
                  <span class="dashboard__card-value">{{ formatSize(ws.sizeBytes) }}</span>
                </div>
                @if (ws.lastBackupDate) {
                  <div class="dashboard__card-row">
                    <span class="dashboard__card-label">Last Backup</span>
                    <span class="dashboard__card-value">{{ formatDate(ws.lastBackupDate) }}</span>
                  </div>
                }
              </div>
              <ng-template #footer>
                <div class="dashboard__card-actions">
                  <p-button label="Open" icon="pi pi-arrow-right" size="small" (onClick)="openWorkspace(ws)" />
                  <p-button label="Backup" icon="pi pi-download" size="small" severity="secondary" [outlined]="true" />
                </div>
              </ng-template>
            </p-card>
          }
        </div>
      </main>
    </div>
  `,
  styles: [`
    .dashboard {
      display: flex;
      height: 100%;
      overflow: hidden;
    }

    .dashboard__sidebar {
      width: var(--aae-sidebar-width, 260px);
      background: var(--aae-surface);
      border-right: 1px solid var(--aae-border);
      display: flex;
      flex-direction: column;
      overflow-y: auto;
      flex-shrink: 0;
    }

    .dashboard__sidebar-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 1rem 1rem 0.5rem;
    }

    .dashboard__sidebar-title {
      font-size: 0.85rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--aae-text-muted);
      margin: 0;
    }

    .dashboard__content {
      flex: 1;
      overflow-y: auto;
      padding: 2rem;
    }

    .dashboard__header {
      margin-bottom: 2rem;
      h1 { margin: 0 0 0.25rem; font-size: 1.5rem; }
    }

    .dashboard__subtitle {
      color: var(--aae-text-muted);
      margin: 0;
    }

    .dashboard__stats {
      display: flex;
      gap: 1rem;
      margin-bottom: 2rem;
      flex-wrap: wrap;
    }

    .dashboard__stat-card {
      display: flex;
      align-items: center;
      gap: 1rem;
      min-width: 180px;
    }

    .dashboard__stat-icon {
      width: 48px;
      height: 48px;
      border-radius: 12px;
      background: var(--aae-accent-muted);
      color: var(--aae-accent);
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 1.25rem;

      &--success {
        background: rgba(34, 197, 94, 0.15);
        color: var(--aae-success);
      }
    }

    .dashboard__stat-value {
      font-size: 1.5rem;
      font-weight: 700;
    }

    .dashboard__stat-label {
      font-size: 0.8rem;
      color: var(--aae-text-muted);
    }

    .dashboard__grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
      gap: 1.25rem;
    }

    .dashboard__card-header {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      padding: 1rem 1.25rem;
      font-weight: 600;
      font-size: 1rem;
      i { color: var(--aae-accent); }
    }

    .dashboard__card-body {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }

    .dashboard__card-row {
      display: flex;
      justify-content: space-between;
      font-size: 0.875rem;
    }

    .dashboard__card-label { color: var(--aae-text-muted); }
    .dashboard__card-value { font-weight: 500; }

    .dashboard__card-actions {
      display: flex;
      gap: 0.5rem;
    }

    .aae-badge--ready {
      background: rgba(34, 197, 94, 0.15);
      color: var(--aae-success);
      border: 1px solid var(--aae-success);
    }

    .aae-badge--restoring {
      background: rgba(245, 158, 11, 0.15);
      color: var(--aae-warning);
      border: 1px solid var(--aae-warning);
    }

    .aae-badge--error {
      background: rgba(239, 68, 68, 0.15);
      color: var(--aae-danger);
      border: 1px solid var(--aae-danger);
    }

    @media (max-width: 768px) {
      .dashboard { flex-direction: column; }
      .dashboard__sidebar { width: 100%; border-right: none; border-bottom: 1px solid var(--aae-border); max-height: 40vh; }
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardComponent {
  protected readonly store = inject(WorkspaceStore);
  private readonly router = inject(Router);

  protected readonly readyCount = computed(
    () => this.store.workspaces().filter((w) => w.status === 'ready').length
  );

  openWorkspace(ws: Workspace): void {
    this.store.selectWorkspace(ws);
    this.router.navigate(['/workspaces']);
  }

  addWorkspace(): void {
    this.router.navigate(['/workspaces/create']);
  }

  formatSize(bytes?: number): string {
    if (!bytes) return '—';
    const mb = bytes / (1024 * 1024);
    return mb >= 1024 ? `${(mb / 1024).toFixed(1)} GB` : `${mb.toFixed(0)} MB`;
  }

  formatDate(date: Date): string {
    return date.toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
  }
}
