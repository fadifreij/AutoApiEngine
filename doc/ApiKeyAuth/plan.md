# Implementation Plan — API Key Authentication for the Dynamic API Controller

> Source of truth: `doc/ApiKeyAuth/requirements.md`
> Scope: **Backend + Frontend full story** (per requirement line 13).

---

## 1. What problem are we solving

Today `DynamicApiController` CRUD endpoints are **anonymous** (`[Authorize]` is commented out at the controller level) and its metadata endpoint is also anonymous. There is no way to give an external client scoped access to a specific dynamic API (a table / view / stored procedure / function + verb + workspace + database).

We already have:
- `ApiKey` table + `ApiKeyController` + `api-keys` FE page — key **SHA-256 hashed** at rest, plaintext shown once at creation.
- `DynamicApiController` with full CRUD + routine execution (SP/Function/View) and a `Verb` classification per object type.

We want to add:
1. A new **`ApiKeyPermission`** table (EF, same pattern as `ApiKey`) that stores *one row per allowed (object + verb)* scope per API key, where a **null `ObjectName` or null `Verb` is a wildcard**, and an `IsDeny` flag for object-scoped denies. Scoped by `workspaceId` + `databaseName`.
2. A **custom filter** in `AutoApiEngine.Presentation` under a new **`Filters`** folder that checks `X-Api-Key` against that table for each dynamic API request.
3. FE UI in a **"Scopes" tab on the API Keys page** (`FE/src/app/dashboard/api-keys/`) to grant/revoke scopes per object, and copy endpoints with the header.
4. Metadata endpoints remain **JWT-only** (requirement line 18).

> Out of scope (explicitly later): alternative auth mechanisms per object — Keycloak identity-provider auth or other external providers (requirement line 3, "implement later").

---

## 2. Decision log (confirmed with user)

| # | Question | Decision |
|---|----------|----------|
| D1 | Auth fallback posture (requirement line 16) | **Allow if no permission row exists** — requests fall through when no `ApiKeyPermission` row matches (object + verb + workspaceId + databaseName). API key required only when a row exists. |
| D2 | Wildcards (`objectName`, `verb`) | **Wildcards are supported.** `ObjectName` and `Verb` are **nullable**; `NULL` means *any*. Four cases: `(NULL, NULL)` all objects/all verbs · `(NULL, verb)` that verb on every object · `(object, NULL)` all verbs on that object · `(object, verb)` exactly that. `NULL` is the wildcard — **not** the string `"*"`, because a real object could be named `*`, and `NULL` avoids translate-on-write *and* translate-on-read. The API accepts `null` and **does not accept `"*"`**. Matching is by the **exact tuple** `(objectName, verb, workspaceId, databaseName)`; see D13 for the fallback consequence. |
| D3 | Where permissions CRUD lives | **New `ApiKeyPermissionController`** (dedicated). `ApiKeyController` untouched. |
| D4 | Verb granularity for Tables | **Per-verb rows** — one row per (key, object, verb). Views/Functions/SPs expose exactly one verb so they map 1:1. |
| D5 | FE extras | **Yes — copy cURL with `X-Api-Key` header** next to each generated endpoint, plus grant/revoke UI. |
| D6 | (pre-set) Single `ApiKeyPermission` table | Single join-table as specified (requirement line 14). |
| D7 | (pre-set) Header name | `X-Api-Key` (requirement line 15). |
| D8 | (pre-set) FE location | **A "Scopes" tab on the API Keys page** (`FE/src/app/dashboard/api-keys/`, route `api-keys`). Per-**key** drill-down: pick a key, then grant scopes per object under it (workspace picker + key picker). The **api-generated page keeps its generated endpoints and Copy cURL exactly as-is** and its "API Key Access" card is removed. |
| D9 | (pre-set) Metadata endpoint | **JWT-only** (requirement line 18). |
| D10 | Org containment on grants | **Enforce twice.** (a) Controller (`§5.3`): the route `apiKeyId`, the body `workspaceId`, and the JWT `organization` claim must all resolve to the **same** organization. (b) Filter (`§4.2`): `apiKey.OrganizationId == workspace.OrganizationId`, or 403 — defence-in-depth so a pre-existing or hand-inserted bad row can never escalate. |
| D11 | JWT on the dynamic CRUD endpoints | **API-key-only in this story — JWT deferred to a later one** (confirmed 2026-09-26). The 5 CRUD actions stay anonymous + `X-Api-Key`; `//[Authorize]` at `DynamicApiController.cs:19` stays commented out (requirement line 15). Therefore the filter **must never read the `organization` claim** — the caller's org is proven solely by `apiKey.OrganizationId` (`§4.2` step 5b). When JWT arrives later, the key remains the *scope* credential and the JWT the *identity* credential; step 5b stays as-is. |
| D12 | Deny support | **`IsDeny` — object-scoped deny only.** `BIT NOT NULL DEFAULT 0`. `IsDeny = 1` is permitted **only when `ObjectName` is non-null**, so exactly two deny shapes exist: **L1** `(key, object, verb, ws, db, deny)` = deny that verb on that object, and **L2** `(key, object, NULL, ws, db, deny)` = deny all verbs on that object. `IsDeny = 1` with `ObjectName == null` is **rejected 400 at every level**. Deny **always wins** over any grant, regardless of specificity — no lattice. `IsDeny` is deliberately **excluded from the unique index**, which is what makes grant+deny at the same 5-tuple impossible. Only **additive** changes are allowed through the CRUD API (`§5.3`). |
| D13 | Fallback granularity | The D1 fallback matches the **EXACT TUPLE**. `requirements.md:15` is literal — *"Api key if row exists else allow"* — so `GetInScopeAsync(objectName, verb, workspaceId, dbName)` returning zero rows ⇒ 200 pass-through. **Accepted consequence: a partial grant protects ONLY the verbs it names.** Granting `GET` on `Orders` means `POST /Orders` and `DELETE /Orders` return **200 to anyone, no key required**. This is load-bearing — the `200` in the `§9` matrix is deliberate, not a typo. |

---

## 3. PART A — Data model (`ApiKeyPermission`)

### 3.1 Entity

New file `BE/AutoApiEngine.Domain/Entities/ApiKeyPermission.cs`:

```csharp
public class ApiKeyPermission : BaseEntity   // BaseEntity gives Id (Guid) + CreatedAt
{
    public Guid ApiKeyId { get; set; }
    public ApiKey ApiKey { get; set; } = null!;            // 1 API key → many permissions

    public string? ObjectName { get; set; }   // NULL = any object   (table / view / sp / function name)
    public string? Verb { get; set; }         // NULL = any verb     (GET | POST | PUT | DELETE)
    public Guid WorkspaceId { get; set; }
    [Required] public string DatabaseName { get; set; } = string.Empty; // denormalized from Workspace at save time
    public bool IsDeny { get; set; }          // NEW — only valid when ObjectName != null
}
```

- **Drop `[Required]` from `ObjectName` and `Verb`** — `null` is the wildcard (D2). `DatabaseName` stays `[Required]`.
- `ApiKey` already has `[Column(TypeName = "VARCHAR")]` string columns — do the same for `ObjectName` / `Verb` / `DatabaseName` for consistency. Those columns also carry an explicit length (`ApiKey.Name` → `[StringLength(250)]`, `ApiKey.Key` → `[StringLength(500)]`); add the matching attribute here rather than inventing a new convention — 250 for `ObjectName` / `Verb` / `DatabaseName` is the safe pick. Note `ObjectName` and `Verb` are now `string?`, so `[Required]` must **not** be reinstated by the nullable-reference-type analyzer.
- `IsDeny` is a `bool` → `BIT NOT NULL DEFAULT 0`.
- Do **not** add navigation props on `ApiKey` back to the list (keep it one-way like `OrganizationId` on `ApiKey`) — actually add `public ICollection<ApiKeyPermission> Permissions` on `ApiKey` **or** keep config-only relationship; follow the existing minimal style (config-only, no back-navigation).

### 3.2 EF configuration (`ApplicationDbContext`)

- Add `public DbSet<ApiKeyPermission> ApiKeyPermissions { get; set; } = null!;`
- In `OnModelCreating`:
  - `modelBuilder.Entity<ApiKeyPermission>().HasOne(p => p.ApiKey).WithMany().HasForeignKey(p => p.ApiKeyId).OnDelete(DeleteBehavior.Cascade);`
    - **NEW and required.** EF Core's default for a required FK is `ClientSetNull`, which **throws** on delete when dependents exist — and `ApiKeyController` has `DELETE /api/keys/{id}`. Without `Cascade`, deleting a key with any scope row blows up at runtime. Cascade makes scopes die with the key. *This is a gap in the design, not something wildcards introduce; it is settled here.*
  - `HasIndex(p => new { p.ApiKeyId, p.ObjectName, p.Verb, p.WorkspaceId, p.DatabaseName }).IsUnique()` — **unchanged**, and `IsDeny` is **deliberately NOT in the index**. That omission is the mechanism that makes grant+deny at the same scope impossible: `(k, Orders, GET, ws, db, deny)` and `(k, Orders, GET, ws, db, grant)` collide on the index, so they cannot coexist. Add a comment in the code saying so — it reads like an oversight otherwise.

### 3.3 Migration

From `BE/AutoApiEngine.ApiServices/`:

```
dotnet ef migrations add AddApiKeyPermission --startup-project . --project ../AutoApiEngine.Persistence
dotnet ef database update --startup-project . --project ../AutoApiEngine.Persistence
```

> Note: two earlier `InitialCreate` migration files are gitignored — new migration is not in that set, commit it.

> **Must prove `NULL` uniqueness in the migration.** SQL Server's `UNIQUE` index treats `NULL` as equal to `NULL`, so `(k, NULL, NULL, W1, Db)` can exist only once per key/workspace/database — which is exactly what we want, but it is *not* standard SQL `WHERE`-clause behaviour, so verify it rather than assume. Test: insert `(k, NULL, NULL, W1, Db)` — **succeeds**; insert the identical row again — **fails with 2605 / 2627**. Also confirm the same for `(k, NULL, GET, …)` and `(k, Orders, NULL, …)`.

### 3.4 Repository

- `BE/AutoApiEngine.ServiceAbstraction/IApiKeyPermissionRepository.cs` (note: file `IGenricRepository.cs` holds `IGenericRepository<T>` — follow the *existing* naming mistakes, don't "fix" them)
- `BE/AutoApiEngine.Services/Repositories/ApiKeyPermissionRepository.cs` implementing base `GenericRepository<ApiKeyPermission>` (same as `ApiKeyRepository`).

Methods needed by filter + controller + FE:

| Method | Purpose |
|---|---|
| `GetInScopeAsync(string objectName, string verb, Guid workspaceId, string databaseName, CancellationToken ct)` | **Replaces `GetByObjectAndScopeAsync`.** The whole in-scope predicate — including both `IS NULL` wildcard branches — expressed in a **single SQL query** (see `§4.2` step 3). |
| `ExistsDenyAsync(Guid apiKeyId, string objectName, string? verb, Guid workspaceId, string databaseName, CancellationToken ct)` | L1 when `verb` is supplied, L2 when `verb` is `null`. |
| `ExistsBroaderScopeAsync(Guid apiKeyId, string? objectName, string? verb, Guid workspaceId, string databaseName, CancellationToken ct)` | The `§5.3` redundancy check — "grant-covered" and "deny-covered". |
| `GetByApiKeyIdAsync(Guid apiKeyId, Ct)` | `GET /api/keys/{id}/permissions` — unchanged. |
| `GetByOrganizationAsync(Guid orgId, Ct)` | Org-wide list for the FE. Should scope by `Workspace.OrganizationId` **as well as** `ApiKey.OrganizationId` so that if a cross-org row ever exists it is at least *visible* to the org owning the workspace (recovery path) — see the note in `§5.1`. |
| `FindActiveKeyByHashAsync(string keyHash, Ct)` | Filter key lookup — `ApiKeys` where `Key == hash && IsActive && (ExpiresAt == null \|\| ExpiresAt > UtcNow)`. Unchanged. |

> `FindActiveKeyByHashAsync` can live on `IApiKeyRepository` instead (key lookup lives with keys) — either placement is fine, but it must be injectable into the filter.

---

## 4. PART B — Custom auth filter (`Filters/ApiKeyAuthFilter.cs`)

### 4.1 Filter type & wiring

- New folder `BE/AutoApiEngine.Presentation/Filters/`.
- `public class ApiKeyAuthFilter : IAsyncAuthorizationFilter` — authorization filters run **before** model binding / the action, can short-circuit with `context.Result`.
- Constructor-inject `IApiKeyPermissionRepository` and `IWorkspaceRepository`.
- Applied only to the **CRUD actions** of `DynamicApiController` via `[ServiceFilter(typeof(ApiKeyAuthFilter))]` on the 5 CRUD action methods (GetList / GetById / Create / Update / Delete). **Not** on the metadata action.

> Alternative considered: apply globally + parse the route pattern. Rejected — the filter is dynamic-API-specific; `[ServiceFilter]` on the controller actions keeps it explicit and leaves every other controller untouched. (`TypeFilter` also fine; `ServiceFilter` needs DI registration — do that in §8 step 8.)

### 4.2 Algorithm

```
OnAuthorizationAsync(context):
  // 1. Route context — UNCHANGED
  if !routeData has "workspaceId" or !has "objectName": pass (return)
  objectName  = RouteData["objectName"]        // already URL-decoded
  verb        = Request.Method.ToUpperInvariant()  // GET/POST/PUT/DELETE
  workspaceId = Guid.Parse(RouteData["workspaceId"])

  // 2. Resolve the workspace's database (MOVED UP — needed by the query)
  workspace = workspaceRepo.GetByIdWithOrganizationAsync(workspaceId)
  dbName    = workspace.DatabaseName

  // 3. In-scope rows for THIS (object, verb, workspace, database), wildcards included.
  //    The whole predicate is in SQL:
  //
  //      (r.ObjectName  IS NULL OR r.ObjectName  = @objectName)
  //    AND (r.Verb        IS NULL OR r.Verb        = @verb)
  //    AND (r.WorkspaceId = @workspaceId)
  //    AND (r.DatabaseName = @dbName)
  //
  rows = repo.GetInScopeAsync(objectName, verb, workspaceId, dbName)
  if rows.empty(): pass (fallback = ALLOW, D1 EXACT TUPLE — D13)

  // 4. Key required — UNCHANGED
  keyValue = Request.Headers["X-Api-Key"].FirstOrDefault()
  if empty → 401 { message: "API key required for this endpoint.", code: "API_KEY_REQUIRED" }

  apiKey = repo.FindActiveKeyByHashAsync(ApiKeyHasher.Hash(keyValue))   // active + not expired
  if apiKey == null → 401 { message: "Invalid, inactive, or expired API key.", code: "API_KEY_INVALID" }

  // 5. Deny wins, then union of grants
  if await repo.ExistsDenyAsync(apiKey.Id, objectName, verb,       workspaceId, dbName) → 403 API_KEY_DENIED   // L1
  if await repo.ExistsDenyAsync(apiKey.Id, objectName, verb: null, workspaceId, dbName) → 403 API_KEY_DENIED   // L2
  if !rows.Any(r => !r.IsDeny && r.ApiKeyId == apiKey.Id) → 403 { message: "API key does not have permission for this endpoint.",
                                                                  code: "API_KEY_FORBIDDEN" }

  // 5b. Org containment (D10) — UNCHANGED
  if apiKey.OrganizationId != workspace.OrganizationId → 403 API_KEY_WRONG_ORGANIZATION

  // 6. Allow + best-effort usage — UNCHANGED
  apiKey.LastUsedAt = DateTime.UtcNow; fire-and-forget save
  pass
```

**Why the predicate must be one SQL query.** The previous shape fetched with `databaseName: null` and then filtered in memory. That shape **cannot express the `IS NULL` branches** — `r.DatabaseName == dbName` in C# throws away every wildcard row, and it lets a row belonging to a *foreign* database influence control flow (a `SalesDb` wildcard would make an `OrdersDb` request key-required). Wildcards make the in-memory variant not merely inefficient but wrong.

**Why two `Any()` calls and no specificity comparator.** Because `IsDeny` is capped to object-scoped (D12), every deny is maximally specific: L1 matches exactly this object+verb, L2 matches exactly this object. There is therefore no lattice to compute — "does a deny match this request?" is two existence checks. Deny is checked **before** the grant union and always wins.

**Grants accumulate — there is no "more specific overrides broader" subtraction.** Access is the **union** of all matching grant rows. A specific row never revokes a broader grant. Removing a broader row does not close the objects a specific row covers; the reverse is also true — the specific row's disappearance leaves the object open to whatever still covers it (or, if nothing does, to the D1 fallback).

**Case-sensitivity.** `ObjectName` comparison now relies on the **column collation**, since the comparison happens in SQL. If the dev database is case-sensitive, either `LOWER()` both sides or a case-insensitive CI collation is required — decide and record it in Step 2.

**Whole-database scope DOES gate.** A row `(k, NULL, NULL, W1, SalesDb)` grants everything *and* makes every object in `SalesDb` key-required. **No extra filter logic is needed** — the wildcard row matches every request, so the `rows.isEmpty()` fallback handles it. Step 5 is unchanged. **Multiple keys may each hold the row** — the database is then allowed for all of them and nobody else.

| Who holds `(k, NULL, NULL, W1, SalesDb)` | with that key | with a different key | anonymous |
|---|---|---|---|
| `k-a` only | 200 | 403 `API_KEY_FORBIDDEN` | 401 `API_KEY_REQUIRED` |
| `k-a` and `k-b` | 200 both | — | 401 |
| nobody | 200 anonymous | 200 | 200 |

**A deny row is ITSELF a gating row.** Adding a deny row makes the predicate match, so the endpoint becomes key-required *for everyone*. If the object had no other grant, adding a deny turns a **200-for-everyone** endpoint into **401-for-everyone**. That is the legitimate "lock this table" intent, but it is a per-key action with a global effect — the FE must say so (`§6.4`).

**Deny rows survive narrowing.** Deleting a broad grant no longer re-opens a denied object, because the deny does not depend on the broader grant existing:

| Rows | `GET /Orders` | `GET /Invoices` |
|---|---|---|
| `(NULL, GET, grant)` + `(Invoices, GET, deny)` | 200 | 403 |
| grant deleted, deny remains | 200 (D1 fallback) | **403** — still locked |

Why step 5b sits where it does, and why it is cheap:
- It is a `Guid` comparison between two values that are **already loaded** — `apiKey.OrganizationId` off the key row resolved at step 4, `workspace.OrganizationId` off the workspace row loaded at step 2. **No extra query** is added to the hot path.
- It is placed **after** the step-5 403s and **before** step 6 (allow + usage record) so a rejected key never writes `LastUsedAt` and never counts as a usage hit.
- Defence in depth per **D10**: the controller check (`§5.3`) stops the bad row from ever being created, but rows can predate this control or be inserted by hand/script/backfill. Step 5b means such a row can **never** be used to cross an org boundary.
- Do **not** try to read the `organization` claim inside the filter — there is no JWT on these requests (see `§4.1`: the dynamic-API client authenticates with the `X-Api-Key` header, not a bearer token).

Key matching notes:
- The header value is hashed with the **exact same SHA-256 hex-lowercase** convention as `ApiKeyController.HashKey` — extract that into a small shared static `ServiceAbstraction/Common/ApiKeyHasher.cs` so the controller and the filter can never drift (controller refactored to call it).
- Verb is the **HTTP method**. This lines up with stored verbs for every object kind: Table GET/POST/PUT/DELETE; View GET; Function GET; SP single GET **or** POST (a POST-classified SP is invoked with HTTP POST → row's verb POST matches).
- `OPTIONS` preflight never matches a verb row, so it still passes — unchanged.

### 4.3 Response codes

| Case | Status | Code |
|---|---|---|
| No row in scope (including no wildcard row) | pass through (200) | — |
| Row in scope, header missing | 401 | `API_KEY_REQUIRED` |
| Row in scope, header hashes to no active key | 401 | `API_KEY_INVALID` |
| Row in scope, key valid, an L1 or L2 deny matches | 403 | `API_KEY_DENIED` |
| Row in scope, key valid, no deny, no grant of its own | 403 | `API_KEY_FORBIDDEN` |
| Row in scope, key valid + holds a grant, org mismatch | 403 | `API_KEY_WRONG_ORGANIZATION` |

---

## 5. PART C — Permissions CRUD backend (`ApiKeyPermissionController`)

- New file `BE/AutoApiEngine.Presentation/Controllers/ApiKeyPermissionController.cs`.
- `[ApiController] [Route("api/keys/{apiKeyId:guid}/permissions")] [Authorize]` — **management is JWT-only** (admins manage permissions; runtime auth on the dynamic endpoint is the filter's job).

### 5.1 Endpoints

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/keys/{apiKeyId}/permissions` | List rows for one key (include object + verb + `IsDeny` + workspace name + db) |
| `GET` | `/api/permissions?organizationId={id}` | Org-wide list for the Scopes UI (each row carries `apiKeyId`, `apiKeyName`, `objectName`, `verb`, `isDeny`, `workspaceId`, `workspaceName`, `databaseName`) |
| `POST` | `/api/keys/{apiKeyId}/permissions` | Body `{ objectName?, verb?, workspaceId, databaseName?, isDeny? }` → create row. `databaseName` optional — **backend fills from `Workspace.DatabaseName`** if omitted (keeps the UI simple while the table always stores it). `409` on an exact duplicate, `400` on a redundant-but-broader scope / deny conflict / invalid field, and **`403` when the workspace belongs to a different organization than the caller** (D10). |
| `DELETE` | `/api/keys/{apiKeyId}/permissions/{permissionId}` | Remove a row |

> Recovery path: `GetByOrganizationAsync` (§3.4) should scope by **`Workspace.OrganizationId` as well as `ApiKey.OrganizationId`** so that if a cross-org row ever exists, it is at least *visible* to the org that owns the workspace (instead of being invisible to both the owning key's org and the workspace's org). Purely a defence/recovery measure — it is not an authorization mechanism, and the org that owns the workspace still cannot use the foreign key.

### 5.2 DTOs (`ServiceAbstraction/DTO`)

- `CreateApiKeyPermissionDto { string? ObjectName, string? Verb, Guid WorkspaceId, string? DatabaseName, bool IsDeny = false }`
- `ApiKeyPermissionDto { Id, ApiKeyId, ApiKeyName?, string? ObjectName, string? Verb, bool IsDeny, Guid WorkspaceId, string? WorkspaceName, string DatabaseName, CreatedAt }`

`ObjectName` and `Verb` are nullable in **both**. `DatabaseName` remains optional on create (backend fills from `Workspace.DatabaseName`).

### 5.3 Validation

**Field-level rules:**

- `ObjectName` — `null` (wildcard), or a non-empty object name that exists in that workspace. **Empty or whitespace-only string → 400**: an empty string is **not** a wildcard, and `"*"` is not accepted either.
- `Verb` — **`null` (wildcard)**, or in `{ GET, POST, PUT, DELETE }` → else **400**.
- `DatabaseName` — filled from `Workspace.DatabaseName` when omitted; when supplied it must match the workspace's database → else **400**.
- `IsDeny = true` requires `ObjectName != null` → else **400** (D12; enforced at controller *and* repository level).

**Org containment (D10 — unchanged):**

- `ApiKeyId` must exist and belong to the caller's organization (JWT `organization` claim, same pattern as `ApiKeyController.Create`).
- `WorkspaceId` must exist **and belong to the caller's organization** — existence alone is not enough.
  - The `organization` claim is a **Name, not an Id**. Preferred check, mirroring `DynamicApiController.VerifyWorkspaceAccessAsync` (lines 42–63): load via `IWorkspaceRepository.GetByIdWithOrganizationAsync(workspaceId, ct)` (Organization eagerly loaded) and compare `workspace.Organization?.Name` against the claim → mismatch is **403**.
  - Alternative accepted, mirroring `ApiKeyController.Create` (lines 79–87) / `WorkspaceController` (lines 258–268): resolve the claim to an `Organization` via `IOrganizationRepository.FindAsync(o => o.Name == orgName, …)`, then compare `workspace.OrganizationId` to `organization.Id`. More robust if organizations can be renamed — prefer this if the rename path is live.
  - Missing/empty `organization` claim → **403**, exactly as the existing helper does.
- **Never trust an org id from the request.** `CreateApiKeyPermissionDto` has no `OrganizationId` and **must not gain one**; the organization comes from the JWT claim only. (Note this deliberately differs from `ApiKeyController.Create`, which prefers `dto.OrganizationId` — that is fine for key creation but is exactly the pattern that must not be repeated here, because a caller-supplied org id is a cross-org grant waiting to happen.)
- Default-allow posture means **no "global" safety check needed**; the duplicate-unique-index is the backstop behind the friendly controller codes below.
- All three orgs must be the **same** org: the route's `{apiKeyId}` org (from the key row), the resolved `workspace.OrganizationId`, and the JWT claim org. A mismatch on any pair is **403** (D10).

**Semantic rules — only ADDITIVE changes are allowed.** Because deny always wins, a naive "create" call can silently override a grant or a deny. The controller refuses every non-additive change up front:

| Change | Verdict | Message |
|---|---|---|
| Grant that adds access nothing else already grants | allow | — |
| Grant already granted by an existing row of the same key | **400** | `"This scope is already covered by a broader grant: {scope}."` |
| Grant already **denied** by an existing deny row | **400** | `"{ObjectName} is denied for this key. Remove the deny before granting."` |
| Deny, nothing in conflict | allow | — |
| Deny already covered by an existing deny row | **400** | `"This deny is already covered by a broader deny: {scope}."` |
| Deny that would override an existing **explicit** grant on the same object | **400** | `"{ObjectName} already has an explicit {Verb} grant. Remove it before denying all verbs."` |

Redundancy formula — row `A` already covers the incoming `B` (same key, workspace, database):

```
(A.ObjectName IS NULL OR A.ObjectName = B.ObjectName)
AND (A.Verb       IS NULL OR A.Verb       = B.Verb)
```

A **deny row is never "covered" by a grant row** — the two checks are separate: an existing grant only blocks a new grant, an existing deny only blocks a new deny (plus the "grant over a deny" and "L2 deny over an explicit grant" cases in the table).

**Status-code guidance (single source of truth):**

| Situation | Status |
|---|---|
| Exact duplicate — same 5-tuple `(ApiKeyId, ObjectName, Verb, WorkspaceId, DatabaseName)`, unique-index backstop | **409** |
| Redundant-but-broader scope, deny conflict, invalid field, empty `ObjectName`, `IsDeny` with `ObjectName == null` | **400** |
| Cross-org attempt | **403** |

> **Redundancy cannot be enforced by the database.** The unique index is on the exact 5-tuple, so `(k, NULL, GET, …)` and `(k, Orders, GET, …)` are *distinct legal rows* — the database will happily accept both. Redundancy is **semantic**, so it lives in the controller plus `ExistsBroaderScopeAsync` in the repository, and nowhere else.

**The attack this prevents.** Without the org-containment check, an Org-1 admin could POST `POST /api/keys/{keyA}/permissions` with `{ objectName: "Users", verb: "GET", workspaceId: "<Org-2's W2>" }`. `keyA` ∈ Org 1 so the existing key check passes, `W2` exists so an existence-only check passes, and the row `(keyA, Users, GET, W2, Db2)` is created. That produces **two** consequences, both bad:
- **Escalation** — the filter (§4.2) does zero org checks, so anyone holding `keyA` can now read Org-2's `Users` table.
- **Invisible cross-org lockout** — the row makes `GetInScopeAsync("Users", "GET", W2, Db2)` non-empty, so Org-2's previously-anonymous endpoint suddenly demands `X-Api-Key` → their clients get **401**. Worse, Org-2 admins can neither see nor delete the row: `GetByOrganizationAsync` joins `ApiKey.OrganizationId`, so a row whose key belongs to Org-1 is filtered out of Org-2's list. The victim cannot discover, list, or remove the row that is breaking their endpoint — a permanent lockout until someone reads the DB directly.

The §5.3 check here is the primary defence (it stops the row being created). The filter's step 5b (§4.2) is the backstop that makes a pre-existing or hand-inserted bad row inert.

---

## 6. PART D — Frontend (Scopes tab on the API Keys page)

> **D8 reversed.** Permission add/remove no longer lives on the api-generated page. It lives in a new **"Scopes" tab** on the **API Keys page** (`FE/src/app/dashboard/api-keys/`, route `api-keys`). Shape is a **per-key drill-down**: pick a key, then grant scopes per object under it. This resolves the old §6.2-vs-§6.3 contradiction — the all-keys toggle matrix reading is dropped, and `selectedKeyId` is now genuinely used. The **api-generated page keeps its generated endpoints and Copy cURL exactly as-is** (D5 unchanged, `YOUR_API_KEY_HERE` placeholder); its "API Key Access" card is **removed**.

### 6.1 `api-key-scopes.service.ts` (new, co-located)

Put the new permission CRUD in a new co-located service, `FE/src/app/dashboard/api-keys/api-key-scopes.service.ts`, following the repo's co-location convention `feature/feature.ts|.html|.scss` and its `inject()` preference. Have the Scopes tab **inject `DynamicApiService`** and call `exploreSchema(workspaceId)` for object enumeration — **reuse, do not move it**. No new backend endpoint is needed: `exploreSchema` already returns `objects: SchemaObject[]` with `type: 'Table' | 'View' | 'StoredProcedure' | 'Function'` (`FE/src/app/dashboard/api-generated/dynamic-api.service.ts:8–21`, method at line 109).

New interfaces:

```ts
interface ApiKeySummary { id: string; name: string; isActive: boolean; expiresAt: string | null }
interface ApiKeyPermissionDto { id, apiKeyId, apiKeyName?, objectName: string | null,
  verb: string | null, isDeny: boolean, workspaceId, workspaceName?, databaseName, createdAt }
```

New methods (all `withCredentials: true`):

| Method | Call |
|---|---|
| `getOrganizationKeys(orgId)` | `GET {api}/keys/organization/{orgId}` — existing `ApiKeyController` endpoint, reused |
| `listPermissions(apiKeyId)` | `GET {api}/keys/{apiKeyId}/permissions` |
| `listOrgPermissions(orgId)` | `GET {api}/permissions?organizationId={orgId}` |
| `createPermission(apiKeyId, body)` | `POST {api}/keys/{apiKeyId}/permissions` |
| `deletePermission(apiKeyId, permissionId)` | `DELETE {api}/keys/{apiKeyId}/permissions/{permissionId}` |

### 6.2 The Scopes grid — state contract

The tab needs a **workspace picker** as well as a **key picker**: the workspace determines `DatabaseName`, so it must be chosen before the grid renders.

```
Reporting-Key  ·  Sales → SalesDb

                    GET       POST      PUT      DELETE
                 ┌─────────┬─────────┬─────────┬─────────┐
◇ All objects    │ ■ ON    │    □    │    □    │    □    │
                 ├─────────┼─────────┼─────────┼─────────┤
  Orders         │ ░       │    □    │    □    │ ■ ON    │
  Invoices       │ ░       │    □    │    □    │    □    │
  OrderSummary   │ ░       │   n/a   │   n/a   │   n/a   │
                 └─────────┴─────────┴─────────┴─────────┘
```

| Symbol | Meaning | Row it maps to | Clickable |
|---|---|---|---|
| `■ ON` | explicit **grant** row exists for exactly this object + verb | exactly one | yes → DELETE |
| `⊘ DENIED` | explicit **deny** row exists (L1 for this verb, or an L2 covering the whole object) | exactly one, or one L2 for the whole row | yes → DELETE the deny |
| `░` | no row of its own; granted by a **broader** row — tooltip *"Granted by: All objects · GET"* | none | **no** — show the covering scope |
| `□` | not granted | none | yes → POST |
| `n/a` | the verb does not exist for this object type | none | no |

> **Invariant: every `■` and every `⊘` maps to exactly one database row.** That is what makes toggle-off unambiguous, and it is why `░` exists as a distinct state rather than rendering `■` and lying about which row is responsible.

An **L2 deny renders the whole object row `⊘`** with a single "remove deny" action, because that cell set maps to exactly one row — this is what keeps the invariant intact at the object level.

A `■` cell can drop to `░` after deleting its row when a broader row still covers it. **That `■` → `░` transition is the visible signal that a broader row took over** — expected behaviour, not a bug, and not something to "fix" in the component.

Verb columns by object type (unchanged mapping):

- `Table` → `GET`, `POST`, `PUT`, `DELETE`
- `View` / `Function` → `GET` only
- `StoredProcedure` → its single classified verb

`n/a` cells are disabled — no HTTP verb reaches them, so a grant on them could never be exercised.

### 6.3 The Scopes tab — component

- New signals: `orgKeys`, `orgPermissions`, `selectedWorkspaceId`, `selectedKeyId`, `schemaObjects`, `permissionBusy`, `permissionError`, `actionMessage`, `activeTab`.
- `selectedWorkspaceId` must resolve `databaseName` (from the workspace fetch) and `schemaObjects` (from `DynamicApiService.exploreSchema`) **before** the grid renders, because the whole grid is scoped to one `(key, workspace, database)`.
- Read-only line showing the resolved `databaseName` for the current workspace so the admin knows the scoping: `Reporting-Key · Sales → SalesDb`.
- Mutations refresh `orgPermissions` after every call; server `400`/`409` messages surface in the error line verbatim (they are written to be user-readable — see `§5.3`).
- Toggle-off always resolves to a `DELETE` of the **one** row behind a `■` or `⊘` cell — never a best-effort guess.
- Button set per cell: `■`/`⊘` → DELETE; `□` → POST grant; `□` on an object that holds an L2 deny → disabled with a hint to remove the deny first (the server would reject it with the "is denied for this key" message).

### 6.4 Mandatory warnings

**Before removing a broad scope.** Because the D1 fallback is the exact tuple, deleting a broad scope **re-opens** everything it covered. The tab must confirm first:

> **Remove "All objects · GET"?**
> Every object in `SalesDb` with no explicit scope becomes open to any caller — requests without a key will succeed. Objects with a `⊘` deny stay locked. Affected: **Invoices**, **OrderSummary**, and any table added later.
> *You will need to grant each remaining object explicitly.*

**Before adding a deny to an object with no other grant.** That turns a 200-for-everyone endpoint into a 401-for-everyone, for *all* keys and anonymous callers. It is a per-key action with a global effect and the UI must say so.

### 6.5 `api-generated` page — net change

- **Copy cURL buttons stay exactly as they are** (D5): table/SP-POST endpoints get `curl -X <METHOD> "<url>" -H "X-Api-Key: YOUR_API_KEY_HERE" -H "Content-Type: application/json" -d '<bodyTemplate>'`, GET endpoints get the header only, placeholder `YOUR_API_KEY_HERE` since plaintext keys are never retrievable (shown once at creation, per existing design).
- The **"API Key Access" card is removed** — the grid moved to the API Keys page.
- Generated endpoints, their metadata block, and `DynamicApiService.exploreSchema` are untouched.

---

## 7. Metadata endpoints stay JWT-only

- Add `[Authorize]` to the metadata action(s) in `DynamicApiController` — currently `GetObjectColumns` (`api/schema/{workspaceId}/columns`).
- Keep the controller-level `[Authorize]` **commented out**; CRUD must stay reachable by anonymous + `X-Api-Key` clients. The metadata action becomes the only JWT-gated route in this controller.
- Confirm the FE `dynamic-api.service.getObjectMetadata` calls still send credentials (`withCredentials: true`) so the dashboard works JWT-authenticated.

---

## 8. Implementation steps (in order — build after each)

| # | Step | Files |
|---|---|---|
| 1 | Add `ApiKeyPermission` entity (nullable `ObjectName`/`Verb` + `IsDeny`) | `Domain/Entities/ApiKeyPermission.cs` |
| 2 | EF `DbSet` + relationship + cascade delete + unique index (`IsDeny` excluded) | `Persistence/Context/ApplicationDbContext.cs` |
| 3 | Migration `AddApiKeyPermission` + apply + prove `NULL`-row uniqueness | `ApiServices/Migrations/*` (new migration, NOT gitignored) |
| 4 | Repository interface + impl (`GetInScopeAsync`, `ExistsDenyAsync`, `ExistsBroaderScopeAsync`) | `ServiceAbstraction/IApiKeyPermissionRepository.cs`, `Services/Repositories/ApiKeyPermissionRepository.cs` |
| 5 | DTOs (nullable + `IsDeny`) | `ServiceAbstraction/DTO/CreateApiKeyPermissionDto.cs`, `ApiKeyPermissionDto.cs` |
| 6 | `ApiKeyHasher` shared static + refactor `ApiKeyController.HashKey` to use it | `ServiceAbstraction/Common/ApiKeyHasher.cs` (or `Services/Security/`), `Presentation/Controllers/ApiKeyController.cs` |
| 7 | `ApiKeyPermissionController` (JWT-only CRUD + additive-only validation) | `Presentation/Controllers/ApiKeyPermissionController.cs` |
| 8 | DI registration | `ApiServices/Providers/ServiceCollectionExtensions.cs` (repo + `ApiKeyAuthFilter`) |
| 9 | `Filters/ApiKeyAuthFilter.cs` + `[ServiceFilter]` on CRUD actions + `[Authorize]` on metadata action | `Presentation/Filters/ApiKeyAuthFilter.cs`, `Presentation/Controllers/DynamicApiController.cs` |
| 10 | Backend verify: `dotnet build BE/AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj` + manual `§9` matrix | — |
| 11 | FE service: `api-key-scopes.service.ts` (DTOs + permission CRUD; reuse `exploreSchema`) | `FE/src/app/dashboard/api-keys/api-key-scopes.service.ts` |
| 12 | FE component: workspace + key pickers, Scopes grid state (`■ ⊘ ░ □ n/a`), deny + broad-scope logic | `FE/src/app/dashboard/api-keys/api-key-scopes.ts` |
| 13 | FE template: Scopes tab grid, tab strip on the API Keys page, mandatory warnings; remove the api-generated "API Key Access" card | `FE/src/app/dashboard/api-keys/api-key-scopes.html`, `.scss`, `api-keys.html` (tab strip), `FE/src/app/dashboard/api-generated/api-generated.html`/`.scss` (card removal only) |
| 14 | Frontend verify: `npm run build` from `FE/` | — |

---

## 9. Verification

**Builds**
- `dotnet build BE/AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj` → 0 errors
- `npm run build` from `FE/` → 0 errors

**Manual (SQL Server) — object = a Table, a View, a GET-classified SP, a POST-classified SP, a Function**

| Scenario | Expect |
|---|---|
| No permission row, no key, anonymous | **200** (fallback allow, D1/D13) |
| Permission row (obj+GET+ws+db), no `X-Api-Key` | **401** `API_KEY_REQUIRED` |
| Permission row, garbage key | **401** `API_KEY_INVALID` |
| Permission row, valid key that lacks the row | **403** `API_KEY_FORBIDDEN` |
| Permission row, valid key that holds the row | **200**; `LastUsedAt` updated |
| Row exists only for `GET`; caller sends `POST` | **200** — no `POST` row is in scope, so the D1 exact-tuple fallback allows it (D13). **This is not a typo to "fix"**: an earlier object-level fallback that would have returned 401 was considered and **rejected** |
| Row `(Orders, NULL)` exists; caller sends `GET` / `POST` / `DELETE` | **401** each (the wildcard row is in scope for all three) |
| Row `(NULL, GET)` exists; caller sends `GET` on any object | **401** (wildcard object) |
| Row `(NULL, NULL)` exists for `k-a` only; `GET` with `k-a` | **200** |
| Row `(NULL, NULL)` exists for `k-a` only; `GET` with `k-b` | **403** `API_KEY_FORBIDDEN` |
| Row `(NULL, NULL)` exists for `k-a` only; `GET` anonymous | **401** `API_KEY_REQUIRED` |
| Row `(NULL, NULL)` exists for `k-a` **and** `k-b`; `GET` with either | **200** both |
| Row `(Orders, GET, deny)`; `GET /Orders` with `k-a` | **403** `API_KEY_DENIED` |
| Row `(Orders, NULL, deny)` (L2); `GET` / `POST` / `PUT` / `DELETE /Orders` with `k-a` | **403** `API_KEY_DENIED` on all four verbs |
| Row `(NULL, GET, grant)` + `(Invoices, GET, deny)`; `GET /Invoices` | **403** `API_KEY_DENIED` |
| Delete the broad grant, deny row remains; `GET /Invoices` | **403** `API_KEY_DENIED` — still locked (deny does not depend on the grant) |
| Add a deny to an object with no other grant; anonymous `GET` | **401** `API_KEY_REQUIRED` (the deny is itself a gating row) |
| Row for SP classified `POST`; request via HTTP POST + key | **200** routine executes |
| Row targeting a different `workspaceId` or `databaseName` | pass-through **200** (row not in scope) |
| Expired / inactive key with valid row | **401** `API_KEY_INVALID` |
| `OPTIONS` preflight on a gated endpoint | passes (matches no verb row) |
| `api/schema/{ws}/columns` with **no JWT** | **401** (metadata JWT-only) |
| `api/schema/{ws}/columns` with valid JWT | **200** unchanged |
| Duplicate permission grant via API (exact 5-tuple) | **409** |
| Grant where a broader row of the same key already grants it | **400** `"This scope is already covered by a broader grant: …"` |
| Grant where a deny row already denies it | **400** `"{ObjectName} is denied for this key. Remove the deny before granting."` |
| Deny already covered by a broader deny | **400** `"This deny is already covered by a broader deny: …"` |
| L2 deny over an existing explicit verb grant on that object | **400** `"{ObjectName} already has an explicit {Verb} grant. Remove it before denying all verbs."` |
| `IsDeny = true` with `ObjectName = null` | **400** |
| `ObjectName = ""` (empty or whitespace) | **400** (not a wildcard) |
| `ObjectName = "*"` | **400** (only `null` is the wildcard) |
| Grant attempt: caller org A, `workspaceId` of org B's workspace | **403** (rejected at `§5.3`, no row created) |
| Row seeded directly in DB: key of org A on org B's workspace + valid `X-Api-Key` | **403** `API_KEY_WRONG_ORGANIZATION` (filter `§4.2` step 5b) |
| Org A grant on org A's workspace | **200/201** unchanged |
| Org A and Org B both hold `Orders/GET` on their own workspaces | both **200** independently; rows coexist (unique index includes `ApiKeyId`) |
| Migration: insert `(k, NULL, NULL, W1, Db)` twice | first **succeeds**, second fails **2605/2627** (SQL Server treats `NULL = NULL` inside a `UNIQUE` index) |
| FE: toggle grant → row appears; toggle revoke → row removed; list refreshes | pass |
| FE: `■` cell becomes `░` after its row is deleted and a broader row still covers it | pass — expected transition, not a bug |
| FE: remove "All objects · GET" → the warning dialog names the affected objects and the re-open consequence | pass |
| FE: existing `api-keys` page regression — key CRUD, delete key with scopes | still works; key delete cascades to scopes |
| FE: api-generated page regression — endpoints + Copy cURL | pass; no "API Key Access" card |
| Existing `api/keys` page regression | still works (controller untouched) |

---

## 10. Risks / notes

- **Default-allow posture (D1)** is the reverse of "secure by default" — it is *exactly* what the requirement specifies. A later inversion (deny-by-default) is a one-line change in the filter step 3 plus a data-backfill, but do NOT do it in this story.
- **The D1 fallback is the EXACT TUPLE (D13), so a partial grant leaves every other verb world-writable.** Granting `GET` on `Orders` means `POST /Orders` and `DELETE /Orders` are **200 to anyone, no key required**. This is load-bearing and intentional — `requirements.md:15` is literal. Do not "fix" the `200` in the `§9` matrix, and do not quietly widen the predicate to `(object, *)`; that would be a policy change nobody asked for.
- **A deny row is itself a gating row.** Adding one makes the predicate match, so the endpoint becomes key-required for *everyone*; on an object with no other grant that is a 200-for-everyone → 401-for-everyone flip. The UI must warn (`§6.4`), and reviewers should read any surprising 401 as "a deny row exists here".
- **`IsDeny` must stay out of the unique index.** It is tempting to add it "for clarity", but doing so would let `(k, Orders, GET, ws, db, deny)` and `(k, Orders, GET, ws, db, grant)` coexist — an ambiguous cell that no `DELETE` can resolve. The index omission is the enforcement; leave the explanatory comment in place.
- **Hash convention drift**: the filter and `ApiKeyController` must share `ApiKeyHasher` (step 6) or keys silently never match.
- **Case-sensitivity is now a SQL/collation question**, not a C# comparison: `ObjectName` is matched inside `GetInScopeAsync`, so the column collation decides. If the dev DB is case-sensitive, either `LOWER()` both sides or a CI collation is required.
- **Route-value decoding**: `objectName` from `RouteData` is already URL-decoded.
- **CORS/OPTIONS**: filter steps the preflight through — `OPTIONS` won't match any verb row, so it always passes; the existing CORS policy already allows the origin/method/headers.
- **DB round-trips**: the filter adds 1–4 queries on dynamic requests (in-scope rows, workspace, api key, up to 2 deny probes). Acceptable for v1; an in-memory cache of org permission sets with a sliding window is the documented follow-up.
- **`Workspace.DatabaseName` is the single source of truth for the stored `databaseName`, filled server-side at grant time.** It is **denormalised**, so renaming a workspace's database silently re-opens everything that was scoped to the old name: the stored rows keep matching the old `databaseName`, the filter queries the new one, and `rows` comes back empty ⇒ the D1 fallback allows the traffic. Any workspace DB-rename path must migrate the `ApiKeyPermission` rows in the same transaction, or be blocked.
- **`LastUsedAt` save is best-effort**: failure to persist usage must not fail the authenticated request.
- **SP POST classification** already exists (`SpVerbClassifier`) — the filter only needs the raw HTTP method; no classification logic duplicated.
- **Migration hygiene**: the two `InitialCreate` files are gitignored; `AddApiKeyPermission` is a normal committed migration. It must also prove the `NULL`-in-`UNIQUE` behaviour (`§3.3`).
- **Redundancy is semantic, not structural.** The unique index cannot see `(k, NULL, GET, …)` vs `(k, Orders, GET, …)`, so `ExistsBroaderScopeAsync` is the only guard. If it is skipped, the grid degrades (a `□` whose click 400s) but no security hole opens — deny still wins, and deny/grant at the same tuple remain impossible.
- No CI workflows exist yet — nothing to update there.
- **Org containment is the only thing preventing cross-org escalation.** `ApiKeyPermission` stores no `OrganizationId` of its own; org isolation is transitive via `ApiKey.OrganizationId`. The `§4.2` step-5b check is therefore **load-bearing, not cosmetic** — if it is ever removed, a single bad row silently grants one org read access to another org's database. Do not "optimize" `GetInScopeAsync` by dropping `workspaceId` from the predicate: org separation currently rests on `workspaceId` being a globally unique Guid.
- **Cascade delete on `ApiKey → ApiKeyPermission` is required**, not cosmetic: EF's default `ClientSetNull` throws when dependents exist, and `DELETE /api/keys/{id}` would 500 on any key that has ever been granted a scope.
- **Workspace-narrowing trap.** Because the predicate requires an exact `workspaceId`, a broad grant on one workspace does **not** reach another workspace. But the reverse trap is real and asymmetric: removing a broad grant re-opens **every other object and every other workspace** the key reached *through that row*, and the confirmation text (`§6.4`) must say so rather than only naming the objects currently visible in the grid — a key can hold `(NULL, NULL, W1, SalesDb)` and `(NULL, NULL, W2, SalesDb2)` in one org, and deleting the first silently re-opens all of `W2`.
- **Known pre-existing issue (out of scope, do not fix here):** `ApiKeyController.GetByOrganizationId` (`ApiKeyController.cs` lines 41–52) does **not** check that the caller's JWT `organization` matches the `{organizationId}` in the route, so any authenticated user can list any org's keys. This story does not touch that method; it is recorded here because the FE helper `getOrganizationKeys()` (§6.1) depends on that endpoint — the Scopes tab's key picker inherits that IDOR — and because the §5.3 org-containment check above must **not** be modelled on it. See `§11`.

---

## 11. Future work (explicitly out of this story)

- Per-object auth mechanism selection: **Keycloak identity-provider auth** or other external provider per object (requirement line 3) — when implemented, the `ApiKeyPermission` table or a sibling `AuthBinding` concept will hold the mechanism choice.
- **Broader-level deny** — `IsDeny = 1` with a null `ObjectName`, i.e. `(NULL, verb, deny)` / `(NULL, NULL, deny)`. Both are excluded today (D12) because a deny is maximally specific by construction, which is what keeps the filter down to two existence checks with no lattice. If a real use case for "deny GET everywhere" appears, it needs a specific/lattice-aware filter — it is not a one-line relaxation of the 400.
- **`IsDeny` for the "deny PUT everywhere" case** specifically: today the cheapest way to make `PUT` unreachable across a database is a grant-everything row plus one L1 deny per object, which is O(objects). A database-wide PUT deny would collapse that to one row and is the most likely first consumer of the item above.
- Key-level and DB-level caching of permissions for the filter.
- **A dedicated ticket for the `ApiKeyController.GetByOrganizationId` IDOR** (`ApiKeyController.cs:41–52` — the route `{organizationId}` is never compared to the caller's JWT `organization` claim, so any authenticated user can list any org's keys). Out of scope for this story, but the Scopes tab's key picker depends on that endpoint, so it should be fixed before the Scopes UI is treated as a security surface.
- **JWT on the dynamic CRUD endpoints is explicitly deferred** (decision **D11**, 2026-09-26). This story is **API-key-only**: the 5 CRUD actions of `DynamicApiController` stay anonymous + `X-Api-Key` (`//[Authorize]` at `DynamicApiController.cs:19` stays commented out), per requirement line 15. Requiring a JWT *in addition to* the key is a later, separate story. Consequences to respect until then:
  - The filter must **never** read the `organization` claim — there is no JWT on these requests, and a *conditional* claim check would be a bypass (omit the `Authorization` header to skip it). The caller's org is proven solely by `apiKey.OrganizationId` (§4.2 step 5b).
  - When JWT is added later, keep the API key as the **scope** credential and the JWT as the **identity** credential; the key's org remains authoritative for containment, so step 5b stays as-is. Do not replace step 5b with a claim comparison.
- **Pointer to prior art:** the plan references `ApiKeyPermission` as the future home of per-object auth-mechanism choice (Keycloak etc.) — the schema should stay extensible, so do not hard-code assumptions beyond the fields specified. In practice this now means: keep `IsDeny` a flag on the same table (rather than splitting a `Deny` table) so that a future auth-mechanism column lands beside it, and keep the wildcard `null` convention intact so a future `"*"`-style scheme never needs a translate layer.
