import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

// ── Data types matching the backend DTOs ──

export interface QueryResultColumn {
  name: string;
  dataType: string;
}

export interface QueryResultRow {
  values: (string | null)[];
}

export interface QueryExecutionResponse {
  success: boolean;
  error: string | null;
  isSelectQuery: boolean;
  columns: QueryResultColumn[];
  rows: QueryResultRow[];
  rowsAffected: number;
  durationMs: number;
  totalRows: number;
}

export interface QueryExecutionRequest {
  workspaceId: string;
  sql: string;
}

@Injectable({ providedIn: 'root' })
export class QueryService {
  private readonly apiUrl = `${environment.apiUrl}/query`;

  constructor(private http: HttpClient) {}

  /**
   * Executes a SQL query against the workspace's database.
   * Supports SELECT (returns tabular results) and DML (returns rows affected).
   *
   * For cancellation, unsubscribe from the returned Observable
   * (the underlying HTTP request will be aborted via Angular's fetch integration).
   */
  execute(
    request: QueryExecutionRequest
  ): Observable<QueryExecutionResponse> {
    return this.http.post<QueryExecutionResponse>(
      `${this.apiUrl}/execute`,
      request,
      {
        withCredentials: true,
      }
    );
  }
}
