import { Component, computed, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { WorkspaceStateService } from '../../shared/workspace-state.service';
import {
  DynamicApiService,
  SchemaObject,
  ObjectMetadata,
  ColumnMetadata,
  ForeignKeyDetail,
  ReferencedByDetail,
  FilterConfig,
  SortConfig,
  DeployedApiDto,
  TestDeployedApiResponse
} from './dynamic-api.service';
import { environment } from '../../../environments/environment';

/** Internal interface for endpoint display info */
interface EndpointInfo {
  method: string;
  methodClass: string;
  path: string;
  fullUrl: string;
  description: string;
  bodyTemplate?: string;
}

@Component({
  selector: 'app-api-generated',
  standalone: true,
  imports: [FormsModule, CommonModule],
  templateUrl: './api-generated.html',
  styleUrl: './api-generated.scss'
})
export class ApiGenerated {
  private readonly workspaceState = inject(WorkspaceStateService);
  readonly dynamicApi = inject(DynamicApiService);

  // ── State ──

  /** Selected workspace ID */
  readonly workspaceId = this.workspaceState.selectedWorkspaceId;

  /** All schema objects from the current workspace */
  readonly schemaObjects = signal<SchemaObject[]>([]);

  /** Currently selected object name */
  readonly selectedObject = signal<string>('');

  /** Currently selected object's full metadata */
  readonly selectedMetadata = signal<ObjectMetadata | null>(null);

  /** Loading flags */
  readonly loadingObjects = signal(false);
  readonly loadingMetadata = signal(false);
  readonly errorMessage = signal<string>('');

  /** Search filter for the object list */
  readonly searchQuery = signal('');

  /** Active tab: 'config' | 'endpoints' */
  readonly activeTab = signal<'config' | 'endpoints'>('config');

  /** Copy feedback */
  readonly copyFeedback = signal('');

  // Exposed for template access
  readonly Math = Math;
  readonly Number = Number;

  // ── Column Selection ──

  /** Set of selected column names */
  readonly selectedColumns = signal<Set<string>>(new Set());

  /** Toggle a column on/off */
  toggleColumn(colName: string): void {
    const s = new Set(this.selectedColumns());
    if (s.has(colName)) s.delete(colName); else s.add(colName);
    this.selectedColumns.set(s);
  }

  /** Select only PK columns */
  selectOnlyPk(): void {
    const meta = this.selectedMetadata();
    if (!meta) return;
    this.selectedColumns.set(new Set(meta.primaryKeyColumns));
  }

  /** Select all columns */
  selectAllColumns(): void {
    const meta = this.selectedMetadata();
    if (!meta) return;
    this.selectedColumns.set(new Set(meta.columns.map(c => c.name)));
  }

  // ── Related Tables (includes) ──

  /** Map of related table name → selected columns */
  readonly includedRelations = signal<Map<string, Set<string>>>(new Map());

  /** Toggle an entire related table on/off */
  toggleRelation(tableName: string, columns: string[]): void {
    const m = new Map(this.includedRelations());
    if (m.has(tableName)) {
      m.delete(tableName);
    } else {
      m.set(tableName, new Set(columns));
    }
    this.includedRelations.set(m);
  }

  /** Toggle a column within an included relation */
  toggleRelationColumn(tableName: string, colName: string): void {
    const m = new Map(this.includedRelations());
    const cols = m.get(tableName);
    if (!cols) return;
    const s = new Set(cols);
    if (s.has(colName)) s.delete(colName); else s.add(colName);
    if (s.size === 0) m.delete(tableName); else m.set(tableName, s);
    this.includedRelations.set(m);
  }

  // ── Filters ──

  readonly filters = signal<FilterConfig[]>([
    { column: '', operator: 'eq', value: '', logic: 'and' }
  ]);

  addFilter(): void {
    this.filters.update(f => [...f, { column: '', operator: 'eq', value: '', logic: 'and' }]);
  }

  removeFilter(index: number): void {
    this.filters.update(f => f.filter((_, i) => i !== index));
  }

  updateFilter(index: number, field: keyof FilterConfig, value: string): void {
    this.filters.update(f => {
      const updated = [...f];
      updated[index] = { ...updated[index], [field]: value };
      return updated;
    });
  }

  // ── Sorting ──

  readonly sorts = signal<SortConfig[]>([
    { column: '', direction: 'asc' }
  ]);

  addSort(): void {
    this.sorts.update(s => [...s, { column: '', direction: 'asc' }]);
  }

  removeSort(index: number): void {
    this.sorts.update(s => s.filter((_, i) => i !== index));
  }

  updateSort(index: number, field: keyof SortConfig, value: string): void {
    this.sorts.update(s => {
      const updated = [...s];
      updated[index] = { ...updated[index], [field]: value as any };
      return updated;
    });
  }

  // ── Paging ──

  readonly pageSize = signal<number>(100);
  readonly currentPage = signal<number>(1);

  // ── Computed values ──

  /** Filtered schema objects based on search */
  readonly filteredObjects = computed(() => {
    const q = this.searchQuery().toLowerCase();
    if (!q) return this.schemaObjects();
    return this.schemaObjects().filter(o =>
      o.name.toLowerCase().includes(q) || o.type.toLowerCase().includes(q)
    );
  });

  /** Available columns for the selected table */
  readonly availableColumns = computed(() => this.selectedMetadata()?.columns ?? []);

  /** Computed select list */
  readonly selectList = computed(() => {
    const cols = Array.from(this.selectedColumns());
    const relations = this.includedRelations();
    relations.forEach((relCols, relName) => {
      relCols.forEach(col => {
        cols.push(`${relName}.${col}`);
      });
    });
    return cols;
  });

  // ── Deployed API State ──

  /** List of deployed APIs for the current workspace */
  readonly deployedApis = signal<DeployedApiDto[]>([]);
  readonly loadingDeployed = signal(false);
  readonly deployError = signal('');
  readonly deploySuccess = signal('');

  /** Active test tab: 'table' | 'json' */
  readonly testViewTab = signal<'table' | 'json'>('table');

  /** Currently selected deployed API for testing */
  readonly selectedDeployedApi = signal<DeployedApiDto | null>(null);

  /** Test results */
  readonly testResults = signal<TestDeployedApiResponse | null>(null);
  readonly testLoading = signal(false);
  readonly testError = signal('');

  /** Deploy the current configuration as a reusable API */
  deployApi(): void {
    const wsId = this.workspaceId();
    const objName = this.selectedObject();
    if (!wsId || !objName) return;

    this.deployError.set('');
    this.deploySuccess.set('');

    const req = {
      workspaceId: wsId,
      objectName: objName,
      selectColumns: this.selectList().length > 0 ? this.selectList().join(',') : undefined,
      filters: this.filters().length > 0 && this.filters()[0].column
        ? JSON.stringify(this.filters().filter(f => f.column))
        : undefined,
      sorts: this.sorts().length > 0 && this.sorts()[0].column
        ? JSON.stringify(this.sorts().filter(s => s.column))
        : undefined,
      pageSize: this.pageSize()
    };

    this.dynamicApi.deployApi(req).subscribe({
      next: () => {
        this.deploySuccess.set(`API "${objName} API" deployed successfully!`);
        this.loadDeployedApis(wsId);
      },
      error: (err) => {
        this.deployError.set('Deploy failed: ' + (err.error?.message ?? err.message));
      }
    });
  }

  /** Load deployed APIs for the current workspace */
  loadDeployedApis(workspaceId: string): void {
    this.loadingDeployed.set(true);
    this.dynamicApi.listDeployedApis(workspaceId).subscribe({
      next: (apis) => {
        this.deployedApis.set(apis);
        this.loadingDeployed.set(false);
      },
      error: () => this.loadingDeployed.set(false)
    });
  }

  /** Delete a deployed API */
  deleteDeployedApi(id: string): void {
    if (!confirm('Delete this deployed API?')) return;
    this.dynamicApi.deleteDeployedApi(id).subscribe({
      next: () => {
        this.deployedApis.update(apis => apis.filter(a => a.id !== id));
        if (this.selectedDeployedApi()?.id === id) {
          this.selectedDeployedApi.set(null);
          this.testResults.set(null);
        }
      }
    });
  }

  /** Select and test a deployed API */
  selectAndTest(api: DeployedApiDto): void {
    this.selectedDeployedApi.set(api);
    this.testResults.set(null);
    this.testError.set('');
    this.testLoading.set(true);

    this.dynamicApi.testDeployedApi(api.id).subscribe({
      next: (res) => {
        this.testResults.set(res);
        this.testLoading.set(false);
      },
      error: (err) => {
        this.testError.set('Test failed: ' + (err.error?.message ?? err.message));
        this.testLoading.set(false);
      }
    });
  }

  /** Get column keys from test results */
  getTestColumns(): string[] {
    const data = this.testResults()?.data;
    if (!data || data.length === 0) return [];
    return Object.keys(data[0]);
  }

  /** Generate a JSON example body for POST/PUT from the selected object's columns */
  readonly bodyTemplate = computed<string>(() => {
    const meta = this.selectedMetadata();
    if (!meta) return '{}';
    return this.generateBodyTemplate(meta.columns);
  });

  /** Generate JSON body template string from column metadata */
  private generateBodyTemplate(columns: ColumnMetadata[]): string {
    const obj: Record<string, any> = {};
    for (const col of columns) {
      // Skip identity/auto-increment columns — they are generated by the database.
      // Non-identity PKs (e.g. manual/varchar PKs) ARE included since the user must provide them.
      if (col.isIdentity) continue;
      obj[col.name] = this.generateExampleValue(col);
    }
    return JSON.stringify(obj, null, 2);
  }

  /** Generate an example value based on column name and data type */
  private generateExampleValue(col: ColumnMetadata): any {
    const type = col.dataType.toLowerCase();
    const name = col.name;

    // Date/time types
    if (type.includes('date') || type.includes('time')) {
      return '2024-01-01T00:00:00';
    }
    // Boolean
    if (type === 'bit') {
      return true;
    }
    // Numeric types
    if (
      type.includes('int') || type.includes('decimal') ||
      type.includes('float') || type.includes('double') ||
      type.includes('numeric') || type.includes('money') ||
      type.includes('real') || type.includes('smallmoney')
    ) {
      return 0;
    }
    // GUID
    if (type.includes('uniqueidentifier') || type.includes('guid')) {
      return '00000000-0000-0000-0000-000000000000';
    }
    // Binary / blob
    if (type.includes('binary') || type.includes('image') || type.includes('bytea')) {
      return null;
    }
    // Default: string — use "example {ColumnName}"
    return `example ${name}`;
  }

  /** Generated endpoint URLs for display */
  readonly generatedUrls = computed<EndpointInfo[]>(() => {
    const wsId = this.workspaceId();
    const objName = this.selectedObject();
    if (!wsId || !objName) return [];

    const baseUrl = this.dynamicApi.buildEndpointUrl(wsId, objName);
    const queryPart = this.computedQueryString();
    const body = this.bodyTemplate();

    return [
      {
        method: 'GET',
        methodClass: 'get',
        path: objName,
        fullUrl: baseUrl + queryPart,
        description: 'List records with filters, paging & column selection'
      },
      {
        method: 'GET',
        methodClass: 'get',
        path: `${objName}/{id}`,
        fullUrl: baseUrl + '/{id}' + queryPart,
        description: 'Get a single record by primary key'
      },
      {
        method: 'POST',
        methodClass: 'post',
        path: objName,
        fullUrl: baseUrl,
        description: 'Create a new record',
        bodyTemplate: body
      },
      {
        method: 'PUT',
        methodClass: 'put',
        path: `${objName}/{id}`,
        fullUrl: baseUrl + '/{id}',
        description: 'Update a record by primary key',
        bodyTemplate: body
      },
      {
        method: 'DELETE',
        methodClass: 'delete',
        path: `${objName}/{id}`,
        fullUrl: baseUrl + '/{id}',
        description: 'Delete a record by primary key'
      }
    ];
  });

  readonly computedQueryString = computed(() => {
    const cols = Array.from(this.selectedColumns());
    const rels = this.includedRelations();
    rels.forEach((relCols, relName) => {
      relCols.forEach(col => cols.push(`${relName}.${col}`));
    });

    return this.dynamicApi.buildQueryString({
      select: cols.length > 0 ? cols : undefined,
      filter: this.filters().filter(f => f.column && f.operator),
      sort: this.sorts().filter(s => s.column),
      page: this.currentPage(),
      pageSize: this.pageSize()
    });
  });

  // ── Load Data ──

  constructor() {
    // Auto-load schema and deployed APIs when workspace changes
    effect(() => {
      const wsId = this.workspaceId();
      if (wsId) {
        this.loadSchemaObjects(wsId);
        this.loadDeployedApis(wsId);
      }
    });
  }

  private loadSchemaObjects(workspaceId: string): void {
    this.loadingObjects.set(true);
    this.errorMessage.set('');
    this.dynamicApi.exploreSchema(workspaceId).subscribe({
      next: (res) => {
        this.schemaObjects.set(res.objects);
        this.loadingObjects.set(false);
      },
      error: (err) => {
        this.errorMessage.set('Failed to load database objects: ' + (err.error?.message ?? err.message));
        this.loadingObjects.set(false);
      }
    });
  }

  onSelectObject(name: string): void {
    this.selectedObject.set(name);
    this.loadingMetadata.set(true);
    this.errorMessage.set('');
    this.selectedColumns.set(new Set());
    this.includedRelations.set(new Map());
    this.filters.set([{ column: '', operator: 'eq', value: '', logic: 'and' }]);
    this.sorts.set([{ column: '', direction: 'asc' }]);
    this.pageSize.set(100);
    this.currentPage.set(1);

    this.dynamicApi.getObjectMetadata(this.workspaceId(), name).subscribe({
      next: (meta) => {
        this.selectedMetadata.set(meta);
        // Default: select all columns
        this.selectedColumns.set(new Set(meta.columns.map(c => c.name)));
        this.loadingMetadata.set(false);
      },
      error: (err) => {
        const metaErr = err.error?.details
          ? `${err.error.message} (${err.error.details})`
          : (err.error?.message ?? err.message);
        this.errorMessage.set('Failed to load metadata: ' + metaErr);
        this.loadingMetadata.set(false);
      }
    });
  }

  // ── Copy URL ──

  async copyUrl(url: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(url);
      this.copyFeedback.set('Copied!');
      setTimeout(() => this.copyFeedback.set(''), 2000);
    } catch {
      this.copyFeedback.set('Failed to copy');
      setTimeout(() => this.copyFeedback.set(''), 2000);
    }
  }

  // ── Object Type Helpers ──

  getObjectIcon(type: string): string {
    switch (type) {
      case 'Table': return '📄';
      case 'View': return '👁️';
      case 'StoredProcedure': return '⚙️';
      case 'Function': return '𝓯';
      default: return '📦';
    }
  }

  objectTypeBadge(type: string): string {
    switch (type) {
      case 'Table': return 'Table';
      case 'View': return 'View';
      case 'StoredProcedure': return 'SP';
      case 'Function': return 'Fn';
      default: return type;
    }
  }
}
