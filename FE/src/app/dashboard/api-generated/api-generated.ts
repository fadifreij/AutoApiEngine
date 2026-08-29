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
  SortConfig
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

  /** Metadata for each related table (columns list). Key is flat name or dot-path for nested (e.g. "TableA.TableB") */
  readonly relatedTablesMeta = signal<Map<string, ColumnMetadata[]>>(new Map());

  /** Parent→children mapping for nested hierarchy display. Key = parent table key, value = child keys */
  readonly relatedTableHierarchy = signal<Map<string, string[]>>(new Map());

  /** Loading state for related table metadata */
  readonly loadingRelated = signal(false);

  /** Color palette for related tables */
  private readonly relationPalette = [
    '#7c3aed', '#059669', '#b45309', '#dc2626',
    '#0891b2', '#d97706', '#be185d', '#1d4ed8'
  ];

  /** Assign a color — top-level related tables get palette colors, nested tables inherit their parent's color */
  readonly tableColor = computed(() => {
    const map = new Map<string, string>();
    const topLevel: string[] = [];
    const hierarchy = this.relatedTableHierarchy();

    for (const key of this.relatedTablesMeta().keys()) {
      if (!key.includes('.')) topLevel.push(key);
    }

    topLevel.forEach((name, i) => {
      const color = this.relationPalette[i % this.relationPalette.length];
      map.set(name, color);
      // Children inherit parent color
      const children = hierarchy.get(name);
      if (children) {
        for (const child of children) {
          map.set(child, color);
        }
      }
    });

    return map;
  });

  /** Toggle an entire related table on/off — when ON, selects all its columns */
  toggleRelation(tableName: string): void {
    const m = new Map(this.includedRelations());
    if (m.has(tableName)) {
      m.delete(tableName);
    } else {
      const relCols = this.relatedTablesMeta().get(tableName);
      if (relCols) {
        m.set(tableName, new Set(relCols.map(c => c.name)));
      }
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

  /** Is a specific related table fully selected (all columns checked)? */
  isRelationFullySelected(tableName: string): boolean {
    const selected = this.includedRelations().get(tableName);
    const all = this.relatedTablesMeta().get(tableName);
    if (!selected || !all) return false;
    return selected.size === all.length;
  }

  /** Load column metadata for all related tables — recursive (grandchildren included). */
  private loadRelatedTablesMeta(workspaceId: string): void {
    const meta = this.selectedMetadata();
    if (!meta) return;

    this.loadingRelated.set(true);
    const allMeta = new Map<string, ColumnMetadata[]>();
    const hierarchy = new Map<string, string[]>();
    const pending = new Set<string>();
    let completed = 0;

    // Seed first-level tables
    for (const fk of meta.foreignKeys ?? []) pending.add(fk.referencedTable);
    for (const ref of meta.referencedBy ?? []) pending.add(ref.table);

    if (pending.size === 0) {
      this.relatedTablesMeta.set(new Map());
      this.relatedTableHierarchy.set(new Map());
      this.loadingRelated.set(false);
      return;
    }

    const loadedKeys = new Set<string>();

    // Collect potential FK pairs for hierarchy from the main table
    for (const fk of meta.foreignKeys ?? []) {
      if (!hierarchy.has(fk.referencedTable)) hierarchy.set(fk.referencedTable, []);
    }
    for (const ref of meta.referencedBy ?? []) {
      if (!hierarchy.has(ref.table)) hierarchy.set(ref.table, []);
    }

    // Each load decrements a counter; when zero, we're done
    let remaining = 0;

    const finishOne = () => {
      remaining--;
      if (remaining <= 0) {
        this.relatedTablesMeta.set(new Map(allMeta));
        this.relatedTableHierarchy.set(new Map(hierarchy));
        this.loadingRelated.set(false);
      }
    };

    const loadOne = (tableKey: string, actualTableName: string) => {
      remaining++;
      this.dynamicApi.getObjectMetadata(workspaceId, actualTableName).subscribe({
        next: (relMeta) => {
          allMeta.set(tableKey, relMeta.columns);
          loadedKeys.add(tableKey);

          // If this is a first-level load, discover nested (grandchild) tables
          if (!tableKey.includes('.')) {
            for (const nestedFk of relMeta.foreignKeys ?? []) {
              const childKey = `${tableKey}.${nestedFk.referencedTable}`;
              if (!loadedKeys.has(childKey) && !allMeta.has(childKey)) {
                const existing = hierarchy.get(tableKey) ?? [];
                existing.push(childKey);
                hierarchy.set(tableKey, existing);
                loadOne(childKey, nestedFk.referencedTable);
              }
            }
            for (const nestedRef of relMeta.referencedBy ?? []) {
              const childKey = `${tableKey}.${nestedRef.table}`;
              if (!loadedKeys.has(childKey) && !allMeta.has(childKey)) {
                const existing = hierarchy.get(tableKey) ?? [];
                existing.push(childKey);
                hierarchy.set(tableKey, existing);
                loadOne(childKey, nestedRef.table);
              }
            }
          }

          finishOne();
        },
        error: () => finishOne()
      });
    };

    // Kick off first-level loads
    for (const tableName of pending) {
      loadOne(tableName, tableName);
    }
  }

  /** Flattened list of all selectable columns (main + active related) for filter/sort dropdowns */
  readonly allSelectableColumns = computed(() => {
    const result: { display: string; value: string; table: string }[] = [];

    // Main table columns (no prefix)
    const mainMeta = this.selectedMetadata();
    if (mainMeta) {
      for (const col of mainMeta.columns) {
        result.push({ display: col.name, value: col.name, table: '' });
      }
    }

    // Related table columns (only for tables currently selected in includedRelations)
    const rels = this.includedRelations();
    const relMeta = this.relatedTablesMeta();
    for (const [tableName] of rels) {
      const cols = relMeta.get(tableName);
      if (cols) {
        for (const col of cols) {
          result.push({ display: `${tableName}.${col.name}`, value: `${tableName}.${col.name}`, table: tableName });
        }
      }
    }

    return result;
  });

  /** Computed: list of related table names that are currently expanded (included) */
  readonly activeRelatedTables = computed(() => {
    return Array.from(this.includedRelations().keys());
  });

  /** Get the display label for a related table key (strips parent prefix for nested) */
  getRelatedTableLabel(key: string): string {
    if (!key.includes('.')) return key;
    const parts = key.split('.');
    return parts[parts.length - 1];
  }

  /** Get the parent key from a dot-path key (empty string if top-level) */
  getRelatedTableParent(key: string): string {
    if (!key.includes('.')) return '';
    return key.substring(0, key.lastIndexOf('.'));
  }

  /** Get child keys for a given parent from the hierarchy */
  getChildKeys(parentKey: string): string[] {
    return this.relatedTableHierarchy().get(parentKey) ?? [];
  }

  /** Check if a given key is a nested (grandchild) table */
  isNestedRelatedTable(key: string): boolean {
    return key.includes('.');
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
    // Auto-load schema when workspace changes
    effect(() => {
      const wsId = this.workspaceId();
      if (wsId) {
        this.loadSchemaObjects(wsId);
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

    this.relatedTablesMeta.set(new Map());
    this.dynamicApi.getObjectMetadata(this.workspaceId(), name).subscribe({
      next: (meta) => {
        this.selectedMetadata.set(meta);
        // Default: select all columns
        this.selectedColumns.set(new Set(meta.columns.map(c => c.name)));
        this.loadingMetadata.set(false);
        // Kick off related table metadata loading
        this.loadRelatedTablesMeta(this.workspaceId());
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
