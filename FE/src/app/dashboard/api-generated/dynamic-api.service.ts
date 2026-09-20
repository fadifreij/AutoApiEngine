import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

// ── DTOs matching the backend ──

export interface SchemaObject {
  name: string;
  type: 'Table' | 'View' | 'StoredProcedure' | 'Function';
  columnCount?: number;
}

export interface SchemaExplorerResponse {
  tablesCount: number;
  viewsCount: number;
  functionsCount: number;
  storedProceduresCount: number;
  databaseSizeBytes: number;
  objects: SchemaObject[];
}

export interface ForeignKeyDetail {
  fkName: string;
  column: string;
  referencedTable: string;
  referencedColumn: string;
  referencedSchema: string;
}

export interface ReferencedByDetail {
  fkName: string;
  table: string;
  column: string;
  referencedColumn: string;
  tableSchema: string;
}

export interface ColumnMetadata {
  name: string;
  dataType: string;
  isNullable: boolean;
  isPrimaryKey: boolean;
  isIdentity: boolean;
}

export interface RoutineParameter {
  name: string;
  dataType: string;
  parameterMode: 'IN' | 'OUT' | 'INOUT';
  hasDefault: boolean;
  defaultValue?: string;
  ordinalPosition: number;
}

export interface ObjectMetadata {
  objectName: string;
  objectType: string;
  schema: string;
  /** "GET" | "POST" for StoredProcedures, "GET" for Views/Functions, null/undefined for Tables */
  verb?: 'GET' | 'POST';
  /** Routine (SP/Function/View) parameter list — always returned by the backend; empty for Tables */
  parameters?: RoutineParameter[];
  columns: ColumnMetadata[];
  primaryKeyColumns: string[];
  foreignKeys: ForeignKeyDetail[];
  referencedBy: ReferencedByDetail[];
}

export interface DynamicApiPaging {
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface DynamicApiListResponse {
  data: Record<string, any>[];
  paging?: DynamicApiPaging;
}

export interface DynamicApiSingleResponse {
  data: Record<string, any>;
}

export interface DynamicApiActionResponse {
  data?: Record<string, any>;
  message: string;
}

/** Response returned when executing a stored procedure or function (mirrors BE DynamicApiExecutionResponse). */
export interface DynamicApiExecutionResponse {
  data?: Record<string, any>;          // scalar-function style result (not part of the BE DTO — reserved)
  resultSets?: Record<string, any>[];  // rows or grouped sets
  outputParams?: Record<string, any>;  // OUT / INOUT param values
  rowsAffected?: number;
}

@Injectable({ providedIn: 'root' })
export class DynamicApiService {
  private readonly schemaUrl = `${environment.apiUrl}/schema`;
  private readonly apiUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  // ── Schema / Metadata ──

  /** List all database objects for a workspace */
  exploreSchema(workspaceId: string, search?: string): Observable<SchemaExplorerResponse> {
    const params: Record<string, string> = {};
    if (search) params['search'] = search;
    return this.http.get<SchemaExplorerResponse>(`${this.schemaUrl}/${workspaceId}`, {
      params,
      withCredentials: true
    });
  }

  /** Get column metadata + FK info for a specific table */
  getObjectMetadata(workspaceId: string, table: string): Observable<ObjectMetadata> {
    return this.http.get<ObjectMetadata>(`${this.schemaUrl}/${workspaceId}/columns`, {
      params: { table },
      withCredentials: true
    });
  }

  // ── Dynamic CRUD ──

  /** Build a full URL for the dynamic API (for display / copying) */
  buildEndpointUrl(workspaceId: string, objectName: string): string {
    return `${environment.apiUrl.replace('/api', '')}/api/${workspaceId}/${objectName}`;
  }

  /** Build query string from filter/sort/page config (for display) */
  buildQueryString(config: {
    select?: string[];
    include?: string[];
    filter?: FilterConfig[];
    sort?: SortConfig[];
    page?: number;
    pageSize?: number;
  }): string {
    const parts: string[] = [];

    // Select — each &select= param (no colon/comma in values, no % encoding needed)
    if (config.select && config.select.length > 0) {
      for (const col of config.select) {
        parts.push(`select=${col}`);
      }
    }

    // Filter — each &filter= param preserves colon separators (no %3A)
    if (config.filter && config.filter.length > 0) {
      for (const f of config.filter) {
        if (f.column && f.operator) {
          if (f.operator === 'eq' && !f.value) continue;
          const prefix = f.logic === 'or' ? 'or:' : '';
          parts.push(`filter=${prefix}${f.column}:${f.operator}:${f.value ?? ''}`);
        }
      }
    }

    // Sort — each &sort= param preserves colon separators (no %3A)
    if (config.sort && config.sort.length > 0) {
      for (const s of config.sort) {
        parts.push(`sort=${s.column}:${s.direction}`);
      }
    }

    // Simple key=value params (no special chars)
    if (config.page && config.page > 1) parts.push(`page=${config.page}`);
    if (config.pageSize && config.pageSize !== 100) parts.push(`pageSize=${config.pageSize}`);

    return parts.length > 0 ? `?${parts.join('&')}` : '';
  }

  /** Build the full endpoint URL with query string (for display) */
  buildFullUrl(workspaceId: string, objectName: string, config: {
    select?: string[];
    include?: string[];
    filter?: FilterConfig[];
    sort?: SortConfig[];
    page?: number;
    pageSize?: number;
  }): string {
    return this.buildEndpointUrl(workspaceId, objectName) + this.buildQueryString(config);
  }

  // ── Routine Execution (StoredProcedure / Function / parametrized View) ──

  /** Execute a GET-classified routine (or parametrized view). Params are serialized as the query string. */
  executeRoutineGet(workspaceId: string, objectName: string, params: Record<string, any>): Observable<DynamicApiExecutionResponse> {
    return this.http.get<DynamicApiExecutionResponse>(`${this.apiUrl}/${workspaceId}/${objectName}`, {
      params: this.trimParams(params),
      withCredentials: true
    });
  }

  /** Execute a POST-classified routine (stored procedure). Params are sent as the JSON body. */
  executeRoutinePost(workspaceId: string, objectName: string, body: Record<string, any>): Observable<DynamicApiExecutionResponse> {
    return this.http.post<DynamicApiExecutionResponse>(`${this.apiUrl}/${workspaceId}/${objectName}`, body, {
      withCredentials: true
    });
  }

  /** Drop null / undefined / empty-string values before sending as query params */
  private trimParams(params: Record<string, any>): Record<string, any> {
    const out: Record<string, any> = {};
    for (const [key, value] of Object.entries(params)) {
      if (value === null || value === undefined || value === '') continue;
      out[key] = value;
    }
    return out;
  }
}

// ── UI Configuration Models ──

export interface FilterConfig {
  column: string;
  operator: string;
  value?: string;
  logic: 'and' | 'or';
}

export interface SortConfig {
  column: string;
  direction: 'asc' | 'desc';
}
