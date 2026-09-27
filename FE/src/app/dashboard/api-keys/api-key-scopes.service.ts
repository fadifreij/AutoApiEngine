import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

// ── DTOs mirroring the backend (ServiceAbstraction/DTO) ──

/**
 * Minimal key shape the Scopes tab needs. Deliberately narrower than the `ApiKey` interface in
 * `api-keys.ts` — the tab never needs the hash or `lastUsedAt`.
 */
export interface ApiKeySummary {
  id: string;
  name: string;
  isActive: boolean;
  expiresAt: string | null;
}

/**
 * One row of `ApiKeyPermission`. `objectName` and `verb` are nullable and a `null` **is** the
 * wildcard (D2) — never the string `"*"`, and never an empty string. `isDeny` is only ever true
 * when `objectName` is non-null (D12).
 */
export interface ApiKeyPermissionDto {
  id: string;
  apiKeyId: string;
  apiKeyName?: string;
  objectName: string | null;
  verb: string | null;
  isDeny: boolean;
  workspaceId: string;
  workspaceName?: string;
  databaseName: string;
  createdAt: string;
}

/** Body for POST /api/keys/{apiKeyId}/permissions. `databaseName` is optional — the backend fills it from the workspace. */
export interface CreateApiKeyPermissionDto {
  objectName: string | null;
  verb: string | null;
  workspaceId: string;
  databaseName?: string | null;
  isDeny?: boolean;
}

/** Minimal workspace shape — the tab only needs the name and the database it resolves to. */
export interface ScopeWorkspace {
  id: string;
  name: string;
  databaseName?: string | null;
}

/**
 * Permission CRUD for the Scopes tab.
 *
 * Kept as its own co-located service rather than folded into `dynamic-api.service.ts` (D8): that
 * service owns *invoking* the dynamic API, this one owns *administering* who may invoke it. Object
 * enumeration is deliberately **not** duplicated here — the component injects `DynamicApiService`
 * and calls `exploreSchema(workspaceId)`, which already returns `objects: SchemaObject[]`.
 *
 * Every call is `withCredentials: true` because these endpoints are `[Authorize]` (JWT-only
 * management API, plan §5) and auth rides on the refresh cookie.
 */
@Injectable({ providedIn: 'root' })
export class ApiKeyScopesService {
  private readonly apiUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  // ── Keys (key picker) ──

  /**
   * Keys belonging to an organization — the existing `ApiKeyController` endpoint, reused as-is
   * (plan §6.1). NOTE: that endpoint has a known pre-existing IDOR (it never compares the route
   * `{organizationId}` to the caller's JWT `organization` claim), so this picker inherits it.
   * Tracked as separate follow-up work; do not model the new permission endpoints on it.
   */
  getOrganizationKeys(orgId: string): Observable<ApiKeySummary[]> {
    return this.http.get<ApiKeySummary[]>(
      `${this.apiUrl}/keys/organization/${encodeURIComponent(orgId)}`,
      { withCredentials: true }
    );
  }

  // ── Workspaces (workspace picker) ──

  /**
   * Workspaces for the workspace picker. Beyond the plan §6.1 method table, but plan §6.2 requires
   * the grid to resolve `databaseName` "from the workspace fetch" before it can render, and the
   * picker is what supplies it. The grid is scoped to one `(key, workspace, database)` triple, so
   * this cannot be skipped.
   */
  getWorkspaces(): Observable<ScopeWorkspace[]> {
    return this.http.get<ScopeWorkspace[]>(`${this.apiUrl}/workspaces`, { withCredentials: true });
  }

  // ── Permissions ──

  /** Rows for a single key. */
  listPermissions(apiKeyId: string): Observable<ApiKeyPermissionDto[]> {
    return this.http.get<ApiKeyPermissionDto[]>(
      `${this.apiUrl}/keys/${encodeURIComponent(apiKeyId)}/permissions`,
      { withCredentials: true }
    );
  }

  /**
   * Org-wide list for the Scopes UI. Every row carries `apiKeyId`, so one call fills the whole
   * grid once a key is picked — no N+1 over keys.
   */
  listOrgPermissions(orgId: string): Observable<ApiKeyPermissionDto[]> {
    return this.http.get<ApiKeyPermissionDto[]>(`${this.apiUrl}/permissions`, {
      params: { organizationId: orgId },
      withCredentials: true
    });
  }

  /**
   * Create a scope row.
   *
   * The backend answers `409` on an exact 5-tuple duplicate and `400` on a redundant-but-broader
   * scope, a deny conflict, or an invalid field. Those messages are written to be user-readable
   * (plan §5.3), so the component surfaces them verbatim — see `extractApiErrorMessage`.
   */
  createPermission(apiKeyId: string, body: CreateApiKeyPermissionDto): Observable<ApiKeyPermissionDto> {
    return this.http.post<ApiKeyPermissionDto>(
      `${this.apiUrl}/keys/${encodeURIComponent(apiKeyId)}/permissions`,
      body,
      { withCredentials: true }
    );
  }

  /**
   * Remove a scope row. Toggle-off always resolves to the single row behind a `■`/`⊘` cell — never
   * a best-effort guess (plan §6.2 invariant).
   */
  deletePermission(apiKeyId: string, permissionId: string): Observable<{ message?: string }> {
    return this.http.delete<{ message?: string }>(
      `${this.apiUrl}/keys/${encodeURIComponent(apiKeyId)}/permissions/${encodeURIComponent(permissionId)}`,
      { withCredentials: true }
    );
  }
}

/**
 * Pull the server's user-readable message out of an error response.
 *
 * The permission endpoints return `{ code, message }` (and `HandleAsync` maps `400`/`403`/`409` to
 * those bodies), so `message` is preferred. The shapes are tried in order and the raw string body is
 * the last resort, which covers MVC's `application/problem+json` (`title`/`detail`) too.
 */
export function extractApiErrorMessage(err: any, fallback: string): string {
  const body = err?.error;
  if (typeof body === 'string' && body.trim()) return body;
  if (body && typeof body === 'object') {
    return body.message || body.detail || body.title || body.error_description || fallback;
  }
  return err?.message || fallback;
}
