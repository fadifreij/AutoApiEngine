import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

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

@Injectable({ providedIn: 'root' })
export class SchemaService {
  private readonly apiUrl = `${environment.apiUrl}/schema`;

  constructor(private http: HttpClient) {}

  /**
   * Explores the schema of the given workspace's database.
   * Optionally filters objects by a search term.
   */
  explore(workspaceId: string, search?: string): Observable<SchemaExplorerResponse> {
    const params: Record<string, string> = {};
    if (search) params['search'] = search;
    return this.http.get<SchemaExplorerResponse>(`${this.apiUrl}/${workspaceId}`, {
      params,
      withCredentials: true
    });
  }

  /**
   * Fetches only summary statistics for the workspace's database.
   */
  getStats(workspaceId: string): Observable<{
    tablesCount: number;
    viewsCount: number;
    functionsCount: number;
    storedProceduresCount: number;
    databaseSizeBytes: number;
  }> {
    return this.http.get<{
      tablesCount: number;
      viewsCount: number;
      functionsCount: number;
      storedProceduresCount: number;
      databaseSizeBytes: number;
    }>(`${this.apiUrl}/${workspaceId}/stats`, {
      withCredentials: true
    });
  }
}
