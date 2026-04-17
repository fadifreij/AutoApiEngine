import { computed, Injectable, signal } from '@angular/core';
import { TreeNode } from 'primeng/api';

export interface Workspace {
  id: number;
  name: string;
  apiKey?: string;
  dbUserName?: string;
  dbPassword?: string;
  isActive?: boolean;
  dbType: 'sqlserver' | 'mysql' | 'postgresql' | 'sqlite';
  lastBackupDate?: Date;
  sizeBytes?: number;
  status: 'ready' | 'restoring' | 'error' | 'empty';
  children: TreeNode[];
}

export interface DbNode {
  key: string;
  label: string;
  type: 'schema' | 'table' | 'view' | 'procedure' | 'function' | 'column';
  icon?: string;
  children?: DbNode[];
  leaf?: boolean;
}

const MOCK_WORKSPACES: Workspace[] = [
  {
    id: 1,
    name: 'SalesDB',
    apiKey: 'AE-SALESDB-STATIC-KEY',
    dbUserName: 'sales_admin',
    dbPassword: '***',
    isActive: true,
    dbType: 'sqlserver',
    lastBackupDate: new Date('2026-04-01'),
    sizeBytes: 524288000,
    status: 'ready',
    children: [
      {
        label: 'Tables', icon: 'pi pi-table', type: 'default', children: [
          { label: 'Customers', icon: 'pi pi-list', type: 'default', leaf: true },
          { label: 'Orders', icon: 'pi pi-list', type: 'default', leaf: true },
          { label: 'Products', icon: 'pi pi-list', type: 'default', leaf: true },
        ]
      },
      {
        label: 'Stored Procedures', icon: 'pi pi-cog', type: 'default', children: [
          { label: 'sp_GetCustomerOrders', icon: 'pi pi-play', type: 'default', leaf: true },
        ]
      },
      {
        label: 'Functions', icon: 'pi pi-code', type: 'default', children: [
          { label: 'fn_CustomerLifetimeValue', icon: 'pi pi-code', type: 'default', leaf: true },
        ]
      },
      {
        label: 'Backups', icon: 'pi pi-history', type: 'default', children: [
          { label: 'backup_2026-04-01.bak', icon: 'pi pi-file', type: 'default', leaf: true },
        ]
      },
    ],
  },
  {
    id: 2,
    name: 'HRDB',
    apiKey: 'AE-HRDB-STATIC-KEY',
    dbUserName: 'hr_admin',
    dbPassword: '***',
    isActive: true,
    dbType: 'postgresql',
    lastBackupDate: new Date('2026-03-28'),
    sizeBytes: 268435456,
    status: 'ready',
    children: [
      {
        label: 'Tables', icon: 'pi pi-table', type: 'default', children: [
          { label: 'Employees', icon: 'pi pi-list', type: 'default', leaf: true },
          { label: 'Departments', icon: 'pi pi-list', type: 'default', leaf: true },
          { label: 'Payroll', icon: 'pi pi-list', type: 'default', leaf: true },
        ]
      },
      {
        label: 'Stored Procedures', icon: 'pi pi-cog', type: 'default', children: [
          { label: 'sp_EmployeeLookup', icon: 'pi pi-play', type: 'default', leaf: true },
        ]
      },
      {
        label: 'Functions', icon: 'pi pi-code', type: 'default', children: [
          { label: 'fn_PayrollTax', icon: 'pi pi-code', type: 'default', leaf: true },
        ]
      },
      {
        label: 'Backups', icon: 'pi pi-history', type: 'default', children: [
          { label: 'backup_2026-03-28.bak', icon: 'pi pi-file', type: 'default', leaf: true },
        ]
      },
    ],
  },
  {
    id: 3,
    name: 'InventoryDB',
    apiKey: 'AE-INVDB-STATIC-KEY',
    dbUserName: 'inventory_admin',
    dbPassword: '***',
    isActive: false,
    dbType: 'mysql',
    sizeBytes: 134217728,
    status: 'ready',
    children: [
      {
        label: 'Tables', icon: 'pi pi-table', type: 'default', children: [
          { label: 'Warehouses', icon: 'pi pi-list', type: 'default', leaf: true },
          { label: 'StockItems', icon: 'pi pi-list', type: 'default', leaf: true },
        ]
      },
      { label: 'Stored Procedures', icon: 'pi pi-cog', type: 'default', children: [] },
      { label: 'Functions', icon: 'pi pi-code', type: 'default', children: [] },
      { label: 'Backups', icon: 'pi pi-history', type: 'default', children: [] },
    ],
  },
];

@Injectable({ providedIn: 'root' })
export class WorkspaceStore {
  // ── Signals ────────────────────────────────────────────────────────────────
  private readonly _workspaces = signal<Workspace[]>(MOCK_WORKSPACES);
  private readonly _selected = signal<Workspace | null>(null);
  private readonly _dbTree = signal<DbNode[]>([]);
  private readonly _uploadProgress = signal<number>(0);
  private readonly _isLoading = signal(false);
  private readonly _sidebarVisible = signal(true);

  // ── Public readonly ────────────────────────────────────────────────────────
  readonly workspaces = this._workspaces.asReadonly();
  readonly selected = this._selected.asReadonly();
  readonly dbTree = this._dbTree.asReadonly();
  readonly uploadProgress = this._uploadProgress.asReadonly();
  readonly isLoading = this._isLoading.asReadonly();
  readonly sidebarVisible = this._sidebarVisible.asReadonly();

  // ── Computed ───────────────────────────────────────────────────────────────
  readonly workspaceCount = computed(() => this._workspaces().length);
  readonly hasWorkspaces = computed(() => this._workspaces().length > 0);

  readonly panelMenuItems = computed(() =>
    this._workspaces().map((ws) => ({
      label: ws.name,
      icon: 'pi pi-database',
      expanded: this._selected()?.id === ws.id,
      command: () => this.selectWorkspace(ws),
      items: [
        { label: 'Upload Database (.bak)', icon: 'pi pi-upload', command: () => { } },
        { label: 'Backup Database', icon: 'pi pi-download', command: () => { } },
      ],
    }))
  );

  readonly treeNodes = computed<TreeNode[]>(() =>
    this._workspaces().map((ws) => ({
      key: String(ws.id),
      label: ws.name,
      icon: 'pi pi-database',
      expanded: this._selected()?.id === ws.id,
      data: ws,
      children: ws.children,
    }))
  );

  // ── Mutations ──────────────────────────────────────────────────────────────
  setWorkspaces(items: Workspace[]): void { this._workspaces.set(items); }
  addWorkspace(w: Workspace): void { this._workspaces.update((list) => [...list, w]); }
  removeWorkspace(id: number): void { this._workspaces.update((list) => list.filter((ws) => ws.id !== id)); }
  selectWorkspace(w: Workspace | null): void { this._selected.set(w); }
  setDbTree(nodes: DbNode[]): void { this._dbTree.set(nodes); }
  setUploadProgress(pct: number): void { this._uploadProgress.set(pct); }
  setLoading(v: boolean): void { this._isLoading.set(v); }
  toggleSidebar(): void { this._sidebarVisible.update((v) => !v); }
}
