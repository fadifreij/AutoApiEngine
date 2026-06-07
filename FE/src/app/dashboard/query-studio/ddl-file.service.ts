import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

// ── Data types matching the backend DTOs ──

export interface DdlFileTreeNode {
  name: string;
  type: 'Folder' | 'File';
  path?: string;
  children?: DdlFileTreeNode[];
}

export interface SaveDdlRequest {
  workspaceId: string;
  folderName: string;
  fileName: string;
  content: string;
}

export interface RenameDdlRequest {
  currentPath: string;
  newName: string;
}

export interface DeleteDdlRequest {
  filePath: string;
}

@Injectable({ providedIn: 'root' })
export class DdlFileService {
  private readonly apiUrl = `${environment.apiUrl}/ddl-files`;

  constructor(private http: HttpClient) {}

  /**
   * Saves SQL content to Saved_DDL/{org}/{db}/{folder}/{file}.sql
   */
  save(request: SaveDdlRequest): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/save`, request, {
      withCredentials: true,
    });
  }

  /**
   * Returns the saved-DDL file tree for the given workspace.
   */
  getTree(workspaceId: string): Observable<DdlFileTreeNode> {
    return this.http.get<DdlFileTreeNode>(`${this.apiUrl}/tree/${workspaceId}`, {
      withCredentials: true,
    });
  }

  /**
   * Reads the content of a saved file by its server path.
   */
  readFile(path: string): Observable<{ content: string }> {
    return this.http.get<{ content: string }>(`${this.apiUrl}/read`, {
      params: { path },
      withCredentials: true,
    });
  }

  /**
   * Renames a saved DDL file or folder on the server.
   */
  rename(request: RenameDdlRequest): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/rename`, request, {
      withCredentials: true,
    });
  }

  /**
   * Deletes a saved DDL file or folder from the server.
   */
  delete(request: DeleteDdlRequest): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/delete`, request, {
      withCredentials: true,
    });
  }
}
