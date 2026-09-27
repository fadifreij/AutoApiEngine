import { isPlatformBrowser } from '@angular/common';
import { Component, computed, inject, OnInit, PLATFORM_ID, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DynamicApiService, SchemaObject } from '../api-generated/dynamic-api.service';
import { AuthService } from '../../shared/auth/auth.service';
import {
  ApiKeyPermissionDto,
  ApiKeyScopesService,
  ApiKeySummary,
  extractApiErrorMessage,
  ScopeWorkspace
} from './api-key-scopes.service';

/** The four HTTP verbs, always rendered as columns. Cells that a verb cannot reach render `na`. */
export const SCOPE_VERBS = ['GET', 'POST', 'PUT', 'DELETE'] as const;
export type ScopeVerb = (typeof SCOPE_VERBS)[number];

/**
 * Cell state (plan §6.2).
 *
 * `on` and `denied`/`denied-l2` are the only states that own a row, and each owns **exactly one** —
 * that invariant is what makes toggle-off an unambiguous `DELETE` rather than a guess. `inherited`
 * owns none: it is the visible signal that a broader row took over after an `on` was deleted.
 */
export type CellState = 'on' | 'denied' | 'denied-l2' | 'inherited' | 'off' | 'na';

export interface GridCell {
  state: CellState;
  /** The single row behind `on` / `denied` / `denied-l2`. `null` for every other state. */
  permissionId: string | null;
  /** The broader grant covering this cell — tooltip source for `inherited`. */
  coveredBy: ApiKeyPermissionDto | null;
  /** Ready-to-render tooltip. */
  hint: string;
}

export interface GridRow {
  /** `null` = the `◇ All objects` wildcard row (D2 — null IS the wildcard, never `"*"`). */
  objectName: string | null;
  label: string;
  isWildcard: boolean;
  cells: GridCell[];
  /** Set when one L2 deny covers this whole object row. */
  l2DenyId: string | null;
}

/** Case-insensitive compare: the DB column collation is `utf8mb4_0900_ai_ci`, so the filter matches
 *  `Employee` against a request for `employee`. The grid must agree or it will offer to create a
 *  duplicate row the backend then rejects as "already covered by a broader grant". */
function sameName(a: string | null, b: string | null): boolean {
  if (a === null || b === null) return a === b;
  return a.toLowerCase() === b.toLowerCase();
}

@Component({
  selector: 'app-api-key-scopes',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './api-key-scopes.html',
  styleUrl: './api-key-scopes.scss'
})
export class ApiKeyScopes implements OnInit {
  private scopes = inject(ApiKeyScopesService);
  private dynamicApi = inject(DynamicApiService);
  private authService = inject(AuthService);
  private platformId = inject(PLATFORM_ID);

  readonly verbs = SCOPE_VERBS;
  readonly orgId = this.authService.organizationId;

  // ── Pickers ──
  workspaces = signal<ScopeWorkspace[]>([]);
  orgKeys = signal<ApiKeySummary[]>([]);
  /** All permission rows for the org. The grid filters down to (key, workspace, database). */
  orgPermissions = signal<ApiKeyPermissionDto[]>([]);
  schemaObjects = signal<SchemaObject[]>([]);

  selectedWorkspaceId = signal<string>('');
  selectedKeyId = signal<string>('');

  // ── Status ──
  loading = signal(false);
  schemaLoading = signal(false);
  permissionBusy = signal(false);
  permissionError = signal('');
  actionMessage = signal('');
  /** Rows for this workspace whose stored `databaseName` no longer matches the workspace's — the
   *  denormalised-db-rename trap from the Risks list. The filter ignores these, so the grid must
   *  not pretend they are in force. */
  staleDatabaseRows = signal<ApiKeyPermissionDto[]>([]);

  // ── Resolved scope (the whole grid hangs off this triple) ──
  readonly selectedWorkspace = computed(() =>
    this.workspaces().find(w => w.id === this.selectedWorkspaceId()) ?? null
  );
  readonly selectedKey = computed(() =>
    this.orgKeys().find(k => k.id === this.selectedKeyId()) ?? null
  );
  readonly databaseName = computed(() => this.selectedWorkspace()?.databaseName ?? '');
  /** Read-only line, e.g. `Reporting-Key · Sales → SalesDb`. */
  readonly scopeHeader = computed(() => {
    const key = this.selectedKey()?.name;
    const ws = this.selectedWorkspace()?.name;
    if (!key || !ws) return '';
    return this.databaseName() ? `${key} · ${ws} → ${this.databaseName()}` : `${key} · ${ws}`;
  });
  /** Both pickers must be chosen before the grid renders — the grid is scoped to one triple. */
  readonly gridReady = computed(() => !!this.selectedKeyId() && !!this.selectedWorkspaceId());

  /**
   * Rows in force for the current (key, workspace, database).
   *
   * `databaseName` is matched exactly on purpose: it is denormalised onto the row at grant time, so
   * a renamed workspace leaves rows the filter will never match. Showing them as active would be a
   * lie — they are surfaced separately in `staleDatabaseRows` instead.
   */
  private activeRows = computed<ApiKeyPermissionDto[]>(() => {
    const keyId = this.selectedKeyId();
    const wsId = this.selectedWorkspaceId();
    const db = this.databaseName();
    if (!keyId || !wsId || !db) return [];
    return this.orgPermissions().filter(
      p => p.apiKeyId === keyId && p.workspaceId === wsId && sameName(p.databaseName, db)
    );
  });

  /** The grid: the `◇ All objects` wildcard row plus one row per object in the workspace. */
  readonly gridRows = computed<GridRow[]>(() => {
    const rows = this.activeRows();
    const objects = this.schemaObjects();

    const wildcard: GridRow = {
      objectName: null,
      label: 'All objects',
      isWildcard: true,
      l2DenyId: null,
      // A deny always names an object (D12), so the wildcard row can never be denied.
      cells: SCOPE_VERBS.map(v => this.resolveCell(rows, null, v, true))
    };

    const objectRows: GridRow[] = objects.map(o => {
      const l2 = rows.find(
        p => p.isDeny && p.verb === null && sameName(p.objectName, o.name)
      );
      return {
        objectName: o.name,
        label: o.name,
        isWildcard: false,
        l2DenyId: l2?.id ?? null,
        cells: SCOPE_VERBS.map(v =>
          this.resolveCell(rows, o.name, v, this.verbApplies(o, v), l2?.id ?? null)
        )
      };
    });

    return [wildcard, ...objectRows];
  });

  ngOnInit(): void {
    if (!isPlatformBrowser(this.platformId)) return;
    this.loadWorkspaces();
    this.loadKeys();
    this.loadPermissions();
  }

  // ── Loading ──────────────────────────────────────────────────────────────

  private loadWorkspaces(): void {
    this.loading.set(true);
    this.scopes.getWorkspaces().subscribe({
      next: list => {
        this.workspaces.set(Array.isArray(list) ? list : []);
        this.loading.set(false);
        this.syncStaleRows();
      },
      error: err => {
        this.loading.set(false);
        this.permissionError.set(extractApiErrorMessage(err, 'Failed to load workspaces.'));
      }
    });
  }

  private loadKeys(): void {
    const orgId = this.orgId();
    if (!orgId) {
      this.permissionError.set('Unable to determine organization.');
      return;
    }
    this.scopes.getOrganizationKeys(orgId).subscribe({
      next: list => this.orgKeys.set(Array.isArray(list) ? list : []),
      error: err =>
        this.permissionError.set(extractApiErrorMessage(err, 'Failed to load API keys.'))
    });
  }

  private loadPermissions(): void {
    const orgId = this.orgId();
    if (!orgId) return;
    this.scopes.listOrgPermissions(orgId).subscribe({
      next: list => {
        this.orgPermissions.set(Array.isArray(list) ? list : []);
        this.syncStaleRows();
      },
      error: err =>
        this.permissionError.set(extractApiErrorMessage(err, 'Failed to load scopes.'))
    });
  }

  /**
   * The workspace determines the object list, so the schema is refetched on every workspace change.
   * Reuses `DynamicApiService.exploreSchema` — object enumeration is not duplicated (plan §6.1).
   */
  onWorkspaceChange(): void {
    // A key is org-scoped, not workspace-scoped, so it survives the switch — but the grid cannot
    // render until both are chosen.
    this.schemaObjects.set([]);
    this.actionMessage.set('');
    this.permissionError.set('');
    this.syncStaleRows();

    const wsId = this.selectedWorkspaceId();
    if (!wsId) return;

    this.schemaLoading.set(true);
    this.dynamicApi.exploreSchema(wsId).subscribe({
      next: res => {
        this.schemaObjects.set(Array.isArray(res?.objects) ? res.objects : []);
        this.schemaLoading.set(false);
        this.syncStaleRows();
      },
      error: err => {
        this.schemaLoading.set(false);
        this.schemaObjects.set([]);
        this.permissionError.set(
          extractApiErrorMessage(err, 'Failed to load database objects for this workspace.')
        );
      }
    });
  }

  onKeyChange(): void {
    this.actionMessage.set('');
    this.permissionError.set('');
    this.syncStaleRows();
  }

  /** Recompute the renamed-database warning whenever any input changes. */
  private syncStaleRows(): void {
    const keyId = this.selectedKeyId();
    const wsId = this.selectedWorkspaceId();
    const db = this.databaseName();
    if (!keyId || !wsId || !db) {
      this.staleDatabaseRows.set([]);
      return;
    }
    this.staleDatabaseRows.set(
      this.orgPermissions().filter(
        p => p.apiKeyId === keyId && p.workspaceId === wsId && !sameName(p.databaseName, db)
      )
    );
  }

  // ── Cell resolution ──────────────────────────────────────────────────────

  /** Does this verb exist for this object type? Must mirror the filter's verb mapping. */
  private verbApplies(obj: SchemaObject, verb: string): boolean {
    switch (obj.type) {
      case 'Table':
        return true;
      case 'View':
      case 'Function':
        return verb === 'GET';
      case 'StoredProcedure':
        // A POST-classified SP is invoked with HTTP POST, so only that column is reachable.
        // `verb` comes from the schema explorer, which classifies it server-side from the SP
        // definition using the same SpVerbClassifier the API-key filter and the dynamic
        // controller use — so this can never disagree with enforcement.
        // Defensive `?? 'GET'` mirrors the metadata endpoint's fallback for an unreadable
        // definition; it also avoids marking the whole row n/a if an older server omits the field.
        return verb === (obj.verb ?? 'GET');
      default:
        return false;
    }
  }

  /**
   * Resolve one cell. Order is load-bearing:
   *  1. `na` — no HTTP verb reaches it, so a grant could never be exercised.
   *  2. L1 deny (this object + this verb).
   *  3. L2 deny (this object, all verbs) — renders the whole object row, one row, one action.
   *  4. Explicit grant for exactly this object + verb — owns exactly one row.
   *  5. `inherited` — a broader grant covers it, so this cell owns nothing.
   *  6. `off`.
   *
   * Deny is checked before grant because the filter checks deny first and deny always wins — a
   * grant and an L2 deny can coexist on the same object, and the UI must show the deny.
   */
  private resolveCell(
    rows: ApiKeyPermissionDto[],
    objectName: string | null,
    verb: string,
    applies: boolean,
    l2DenyId: string | null = null
  ): GridCell {
    if (!applies) {
      return { state: 'na', permissionId: null, coveredBy: null, hint: 'This verb does not apply to this object type.' };
    }

    // 2. L1 deny
    const l1 = rows.find(p => p.isDeny && p.verb === verb && sameName(p.objectName, objectName));
    if (l1) {
      return { state: 'denied', permissionId: l1.id, coveredBy: null, hint: 'Denied for this verb. Click to remove the deny.' };
    }

    // 3. L2 deny covers the whole object row
    if (l2DenyId) {
      const l2 = rows.find(p => p.id === l2DenyId);
      return {
        state: 'denied-l2',
        permissionId: l2DenyId,
        coveredBy: null,
        hint: 'Denied for all verbs on this object. Click to remove the deny.'
      };
    }

    // 4. explicit grant
    const own = rows.find(p => !p.isDeny && p.verb === verb && sameName(p.objectName, objectName));
    if (own) {
      return { state: 'on', permissionId: own.id, coveredBy: null, hint: 'Granted. Click to revoke.' };
    }

    // 5. covered by a broader grant
    const broader = rows.find(
      p => !p.isDeny && this.covers(p, objectName, verb)
    );
    if (broader) {
      return {
        state: 'inherited',
        permissionId: null,
        coveredBy: broader,
        hint: `Granted by: ${this.scopeLabel(broader)}`
      };
    }

    return { state: 'off', permissionId: null, coveredBy: null, hint: 'Not granted. Click to grant.' };
  }

  /**
   * The redundancy formula from plan §5.3, reused for the grid: row `a` covers `(object, verb)` when
   * `a` is on the same object or wider, and on the same verb or wider.
   *
   * The wildcard row is `objectName === null`. A grant on a *specific* object does **not** cover the
   * wildcard row — `(Orders, GET)` is narrower than `(null, GET)` — and this formula gets that right
   * because `a.objectName === null` is required when the target object is null.
   */
  private covers(a: ApiKeyPermissionDto, objectName: string | null, verb: string): boolean {
    const objectMatches = a.objectName === null || sameName(a.objectName, objectName);
    const verbMatches = a.verb === null || a.verb === verb;
    return objectMatches && verbMatches;
  }

  // ── Labels ───────────────────────────────────────────────────────────────

  /** `All objects · GET` style label, matching the server's wording. */
  scopeLabel(p: ApiKeyPermissionDto): string {
    const obj = p.objectName ?? 'All objects';
    const verb = p.verb ?? 'all verbs';
    return p.isDeny ? `${obj} · ${verb} (deny)` : `${obj} · ${verb}`;
  }

  objectTypeLabel(o: SchemaObject): string {
    switch (o.type) {
      case 'Table': return 'Table';
      case 'View': return 'View';
      case 'Function': return 'Function';
      case 'StoredProcedure': return `Stored procedure · ${o.verb ?? 'GET'}`;
      default: return o.type;
    }
  }

  cellSymbol(state: CellState): string {
    switch (state) {
      case 'on': return '■';
      case 'denied':
      case 'denied-l2': return '⊘';
      case 'inherited': return '░';
      case 'na': return 'n/a';
      default: return '□';
    }
  }

  cellLabel(state: CellState): string {
    switch (state) {
      case 'on': return 'Granted';
      case 'denied': return 'Denied';
      case 'denied-l2': return 'Denied (all verbs)';
      case 'inherited': return 'Granted by a broader scope';
      case 'na': return 'Not applicable';
      default: return 'Not granted';
    }
  }

  // ── Mutations ────────────────────────────────────────────────────────────

  /**
   * Toggle a cell.
   *
   * `on` / `denied` / `denied-l2` always resolve to a `DELETE` of the **one** row behind the cell —
   * never a best-effort guess. `inherited` is not clickable precisely because it owns no row.
   * `off` creates either a grant or, when `deny` is set, an L1 deny.
   */
  onCellClick(row: GridRow, verb: string): void {
    const idx = this.verbs.indexOf(verb as ScopeVerb);
    const target = idx >= 0 ? row.cells[idx] : undefined;
    if (!target || target.state === 'na' || target.state === 'inherited') return;

    const keyId = this.selectedKeyId();
    const wsId = this.selectedWorkspaceId();
    if (!keyId || !wsId) return;

    if (target.permissionId) {
      this.removePermission(keyId, target.permissionId, row, verb, target.state);
    } else {
      this.createGrant(keyId, wsId, row, verb);
    }
  }

  private createGrant(keyId: string, wsId: string, row: GridRow, verb: string): void {
    this.permissionBusy.set(true);
    this.permissionError.set('');
    this.actionMessage.set('');
    this.scopes
      .createPermission(keyId, { objectName: row.objectName, verb, workspaceId: wsId, isDeny: false })
      .subscribe({
        next: () => {
          this.permissionBusy.set(false);
          this.actionMessage.set(`Granted ${row.label} · ${verb}.`);
          this.refresh();
        },
        error: err => {
          this.permissionBusy.set(false);
          // 400 "already covered by a broader grant" / "is denied for this key" and 409 duplicate
          // are all written to be user-readable — surface them verbatim (plan §5.3).
          this.permissionError.set(extractApiErrorMessage(err, 'Failed to grant scope.'));
        }
      });
  }

  /**
   * Create a deny on an object.
   *
   * Two mandatory warnings (plan §6.4), because a deny row is **itself a gating row**: adding one
   * makes the filter's predicate match, so the endpoint becomes key-required for *everyone*.
   */
  addDeny(row: GridRow): void {
    const keyId = this.selectedKeyId();
    const wsId = this.selectedWorkspaceId();
    if (!keyId || !wsId || row.objectName === null) return;

    const rows = this.activeRows();
    const anyGrantCovers = rows.some(p => !p.isDeny && this.covers(p, row.objectName, 'GET'));

    if (!anyGrantCovers) {
      const confirmed = confirm(
        `Deny all verbs on "${row.label}"?\n\n` +
          `"${row.label}" has no grant covering it, so this is currently open to any caller.\n` +
          `Adding a deny makes the endpoint require an X-Api-Key from EVERYONE — anonymous callers\n` +
          `and every other key get 401, and this key gets 403.\n\n` +
          `This is a per-key action with a global effect.`
      );
      if (!confirmed) return;
    } else if (
      !confirm(
        `Deny all verbs on "${row.label}"?\n\n` +
          `This key's own access to "${row.label}" will be revoked (403 API_KEY_DENIED).\n` +
          `Deny always wins over any grant, including broader ones.`
      )
    ) {
      return;
    }

    this.permissionBusy.set(true);
    this.permissionError.set('');
    this.actionMessage.set('');
    this.scopes
      .createPermission(keyId, { objectName: row.objectName, verb: null, workspaceId: wsId, isDeny: true })
      .subscribe({
        next: () => {
          this.permissionBusy.set(false);
          this.actionMessage.set(`Denied all verbs on ${row.label}.`);
          this.refresh();
        },
        error: err => {
          this.permissionBusy.set(false);
          this.permissionError.set(extractApiErrorMessage(err, 'Failed to deny scope.'));
        }
      });
  }

  private removePermission(
    keyId: string,
    permissionId: string,
    row: GridRow,
    verb: string,
    state: CellState
  ): void {
    const target = this.orgPermissions().find(p => p.id === permissionId);
    if (!target) return;

    // Mandatory warning before removing a broad scope (plan §6.4).
    if (this.isBroad(target) && !this.confirmBroadRemoval(target, row)) return;

    this.permissionBusy.set(true);
    this.permissionError.set('');
    this.actionMessage.set('');
    this.scopes.deletePermission(keyId, permissionId).subscribe({
      next: () => {
        this.permissionBusy.set(false);
        this.actionMessage.set(
          state === 'denied' || state === 'denied-l2'
            ? `Removed the deny on ${row.label}.`
            : `Revoked ${row.label} · ${verb}.`
        );
        this.refresh();
      },
      error: err => {
        this.permissionBusy.set(false);
        this.permissionError.set(extractApiErrorMessage(err, 'Failed to remove scope.'));
      }
    });
  }

  /** A row is "broad" when it widens either axis — a wildcard object or a wildcard verb. */
  private isBroad(p: ApiKeyPermissionDto): boolean {
    return p.objectName === null || p.verb === null;
  }

  /**
   * The re-open warning (plan §6.4).
   *
   * Because the D1 fallback matches the **exact tuple**, deleting a broad scope re-opens everything
   * it covered. The list is computed from real data, and — per the workspace-narrowing trap in the
   * Risks list — it must also name the **other workspaces** this key reached through the same row,
   * not just the objects visible in the current grid: a key can hold `(NULL, NULL, W1, Db1)` and
   * `(NULL, NULL, W2, Db2)`, and deleting the first silently re-opens all of W2.
   */
  private confirmBroadRemoval(target: ApiKeyPermissionDto, row: GridRow): boolean {
    const rows = this.activeRows();
    const others = rows.filter(p => p.id !== target.id);

    // Objects in THIS workspace that lose their only covering grant and have no deny.
    const affectedHere: string[] = [];
    for (const o of this.schemaObjects()) {
      if (this.verbApplies(o, 'GET') || this.verbApplies(o, 'POST')) {
        const stillGranted = others.some(
          p => !p.isDeny && this.covers(p, o.name, 'GET')
        );
        const denied = others.some(p => p.isDeny && sameName(p.objectName, o.name));
        if (!stillGranted && !denied) affectedHere.push(o.name);
      }
    }

    // Other workspaces this key reached through a row the deleted scope also covers.
    const otherWorkspaces = new Set<string>();
    for (const p of this.orgPermissions()) {
      if (p.apiKeyId !== target.apiKeyId) continue;
      if (p.workspaceId === this.selectedWorkspaceId()) continue;
      if (p.isDeny) continue;
      if (this.covers(p, target.objectName, target.verb ?? 'GET')) {
        otherWorkspaces.add(p.workspaceName || p.workspaceId);
      }
    }

    const lines: string[] = [];
    lines.push(`Remove "${this.scopeLabel(target)}"?`);
    lines.push('');
    lines.push(
      'The default policy is "allow unless a row exists". Deleting this row therefore re-opens ' +
        'everything it covered to ANY caller — requests without a key will start succeeding.'
    );
    if (affectedHere.length) {
      lines.push('');
      lines.push(`Open in ${this.databaseName()}: ${affectedHere.join(', ')}.`);
    }
    if (otherWorkspaces.size) {
      lines.push('');
      lines.push(
        `Also re-opens this key's reach in other workspace(s): ${Array.from(otherWorkspaces).join(', ')}.`
      );
    }
    lines.push('');
    lines.push('Objects with a deny stay locked.');
    lines.push('You will need to grant each remaining object explicitly.');

    return confirm(lines.join('\n'));
  }

  /** Re-read permissions after every mutation so the grid can never show a stale row. */
  private refresh(): void {
    this.loadPermissions();
  }

  // ── Template helpers ─────────────────────────────────────────────────────

  hasStaleRows(): boolean {
    return this.staleDatabaseRows().length > 0;
  }
}
