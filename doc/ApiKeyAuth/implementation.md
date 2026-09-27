# Implementation — API Key Authentication for the Dynamic API Controller

> Companion to `doc/ApiKeyAuth/plan.md` (design + decision log) and `doc/ApiKeyAuth/requirements.md` (source of truth).
> **How to use:** one section per plan step. Each step has a **Status** marker — flip it to `🚧 IN PROGRESS` while working, `✅ DONE` once finished and verified. Work is committed directly to `master` (no branches). `dotnet build` must stay green after every step.
> **Current state (verified 2026-09-27):** Steps 1–13 are implemented; `dotnet build` is green (0 errors) and `npm run build` is green (15 prerendered routes). Step 10 (runtime verification) is **✅ DONE** — filter matrix **38/38**, Step 7 controller matrix **11/11**, with the two bugs it surfaced (Bug 1 in this story, Bug 2 pre-existing in `Program.cs`) both fixed. Steps 11–13 (the whole Scopes tab + D5 cURL) are **✅ DONE**; Step 12 needed one **additive backend** change — `SchemaObjectDto.Verb` — because `exploreSchema()` did not expose the per-object verb, without which every stored procedure rendered as four `n/a` cells and no scope on an SP could be granted (see the deviation note under Step 12). The Scopes data loop is proven end-to-end against a live backend. **Step 14 is 🚧 IN PROGRESS**: the remaining work is the browser click-through of the Scopes tab, which is the only part not yet exercised. The **prerequisites were already in place** (ApiKey table, controller, repo, migration, FE page — committed in `407ccc2`/`af5fb3d`/`328132a`).
>
> ### ⚠️ Environment divergences from this document (read before trusting Step 3 / Step 4)
>
> This plan was written assuming **SQL Server**. The dev metadata database is actually **MySQL 8**
> (`DatabaseProvider=MySql`, `DbPortal` @ `localhost:3307`, collation `utf8mb4_0900_ai_ci` —
> case- and accent-**in**sensitive). Three consequences, all verified live against the dev container:
>
> | # | Plan assumed | Reality | Resolution |
> |---|---|---|---|
> | **A** | Step 1: `StringLength(250)` on `ObjectName`/`Verb`/`DatabaseName` | A 5-column UNIQUE index at 250 needs 3288 bytes in utf8mb4; InnoDB caps a key at 3072 → **`ERROR 1071 Specified key was too long`**. The migration cannot even be created. | **Approved deviation:** lengths are **128 / 32 / 128** (1440 bytes — fits MySQL's 3072 *and* SQL Server's 1700). 128 is also the real SQL Server max identifier length, so nothing legitimate is lost. The reason is recorded in a comment on the entity so nobody "fixes" it back. |
> | **B** | Step 3 §3: prove a duplicate `(k, NULL, NULL, W1, Db)` insert fails with **2605/2627** | That is SQL Server's `NULL = NULL` rule. **MySQL treats NULLs as DISTINCT** in a UNIQUE index, so the duplicate is *accepted* (verified: 2 rows survive; the non-NULL duplicate correctly fails with `ERROR 1062`). | **Approved deviation:** the **409 exact-duplicate contract is enforced explicitly** by the controller via `IApiKeyPermissionRepository.ExistsExactScopeAsync` before insert, so it behaves identically on both providers. The unique index is kept as a backstop for the non-NULL case. Without this, wildcard duplicates would break both the 409 contract and Step 12's "every `■` maps to exactly one database row" invariant. |
> | **C** | Step 3: `IsDeny` as `BIT NOT NULL DEFAULT 0`; Step 4 §5: "decide the case-sensitivity question" | MySQL maps `bool` → `tinyint(1) NOT NULL`. The collation is already `utf8mb4_0900_ai_ci`. | Resolved: no `LOWER()` anywhere (it would only defeat the index) — case-insensitivity comes from the collation, noted in a comment at the comparison site. |
>
> **Also note:** migrations are emitted into `BE/AutoApiEngine.ApiServices/Migrations/` (the `MigrationsAssembly` is `AutoApiEngine.ApiServices`), so Step 3's `--project ../AutoApiEngine.Persistence` **fails** — EF requires the target project to match the migrations assembly. Working form, from `BE/AutoApiEngine.ApiServices/`:
> ```
> dotnet ef migrations add AddApiKeyPermission --startup-project .
> dotnet ef database update --startup-project .
> ```

---

## Status Legend

| Marker | Meaning |
|---|---|
| ⬜ PENDING | Not started |
| 🚧 IN PROGRESS | Currently being worked on |
| ✅ DONE | Finished + builds pass |

---

## 0. Verified prerequisites (already in the codebase — do NOT redo)

These were confirmed against the current `master` (2026-09-24):

| Prerequisite | Where | Verified details |
|---|---|---|
| `ApiKey` entity | `BE/AutoApiEngine.Domain/Entities/ApiKey.cs` | `BaseEntity` (Id Guid + CreatedAt); `VARCHAR` `Name` with `[StringLength(250)]`; `Key` = **SHA-256 hex-lowercase** hash with `[StringLength(500)]`, raw never stored; `ExpiresAt`, `LastUsedAt`, `IsActive`, `OrganizationId` + `Organization` nav |
| Hashing convention | `ApiKeyController.HashKey` (lines 157–162) | `SHA256` → `Convert.ToHexString(...).ToLowerInvariant()` — **originally `private static` inside the controller; Step 6 extracts it** to a shared `ApiKeyHasher` so the filter and controller can never drift |
| Key generation | `ApiKeyController.GenerateKey` (lines 151–155) | 64 hex chars (`Guid N` × 2); plaintext returned **only** at creation |
| `ApiKeyController` | `BE/AutoApiEngine.Presentation/Controllers/ApiKeyController.cs` | `[Route("api/keys")] [Authorize]`; has `GET api/keys/organization/{organizationId}` (reused by FE plan §6.1); organization resolved from `dto.OrganizationId` or JWT `organization` claim; `DELETE api/keys/{id}` exists — hence the cascade requirement in Step 2 |
| `IApiKeyRepository` / impl | `ServiceAbstraction/IApiKeyRepository.cs`, `Services/Repositories/ApiKeyRepository.cs` | Extends `IGenericRepository<ApiKey>` (interface file is `IGenricRepository.cs` — naming mistake kept by convention); `GetByOrganizationIdAsync` present; registered `services.AddScoped<IApiKeyRepository, ApiKeyRepository>()` (line 19) |
| EF + migration | `Persistence/Context/ApplicationDbContext.cs`, migration `20260829141608_ApiKeyEntity` | `DbSet<ApiKey> ApiKeys` (line 41); `HasOne(k => k.Organization).WithMany()` config (lines 80–83); migration is **committed** (not in the gitignored `InitialCreate` set) |
| `DynamicApiController` | `Presentation/Controllers/DynamicApiController.cs` | Line 19: `//[Authorize]` **commented out** → CRUD is anonymous today, exactly the state the plan fixes. Line 73: `GetObjectColumns` (`api/schema/{workspaceId:guid}/columns`) anonymous, guarded only by the manual `VerifyWorkspaceAccessAsync` org check → needs `[Authorize]` (plan §7) |
| FE api-keys page | `FE/src/app/dashboard/api-keys/api-keys.ts|.html|.scss` + route `app.routes.ts` line 22 | Exists and works — key list, create form, delete. **D8 reversed**: this page gains a **Scopes tab** (Steps 11–13). Backend `ApiKeyController` stays untouched (D3) apart from the hasher extraction |
| `DynamicApiService.exploreSchema` | `FE/src/app/dashboard/api-generated/dynamic-api.service.ts` — `SchemaObject` at lines 8–11, response `objects: SchemaObject[]` at 14–21, method at line 109 | Already returns `type: 'Table' \| 'View' \| 'StoredProcedure' \| 'Function'` per object. **Reuse, do not move** — the Scopes tab injects `DynamicApiService`. No new backend endpoint needed for object enumeration |
| `X-Api-Key` header | — | **No handling anywhere** — zero matches in BE or FE. The filter is greenfield |

**Baseline verification (passes → safe to start):**
- Backend: `dotnet build BE/AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj` → ✅ 0 errors
- Frontend: `npm run build` from `FE/` → ✅ success (SSR bundle, 15 prerendered routes)

---

## Step 1 — `ApiKeyPermission` entity

**Status:** ✅ DONE

**Goal:** The join table row: one scope per (ApiKey → object + verb + workspace + database), where a null object or null verb is a wildcard.

**File:** `BE/AutoApiEngine.Domain/Entities/ApiKeyPermission.cs` (new)

**Tasks:**
1. Per plan §3.1:
   ```csharp
   public class ApiKeyPermission : BaseEntity   // Id (Guid) + CreatedAt from BaseEntity
   {
       public Guid ApiKeyId { get; set; }
       public ApiKey ApiKey { get; set; } = null!;            // 1 key → many permissions

       public string? ObjectName { get; set; }   // NULL = any object (table/view/sp/function name)
       public string? Verb { get; set; }         // NULL = any verb   (GET | POST | PUT | DELETE)
       public Guid WorkspaceId { get; set; }
       [Required] public string DatabaseName { get; set; } = string.Empty; // denormalized at save time
       public bool IsDeny { get; set; }          // NEW — only valid when ObjectName != null
   }
   ```
2. **Drop `[Required]` from `ObjectName` and `Verb`** (D2) — `null` *is* the wildcard. Only `DatabaseName` stays `[Required]`. Watch the nullable-reference-type analyzer: the columns are `string?`, so a stray `[Required]` is both wrong and noisy.
3. Follow `ApiKey`'s column style: `[Column(TypeName = "VARCHAR")]` on `ObjectName` / `Verb` / `DatabaseName`, and add the length attribute that convention already uses (`ApiKey.Name` → `[StringLength(250)]`, `ApiKey.Key` → `[StringLength(500)]`) — use 250 for these three. Do not invent a new length convention.
4. `IsDeny` is a plain `bool` → `BIT NOT NULL DEFAULT 0`. It is **only** valid when `ObjectName != null` (D12); that invariant is enforced in Step 4/7, not here.
5. One-way relationship per plan §3.1 note — **config-only** (`WithMany()` with no back-navigation list on `ApiKey`), consistent with the existing minimal style.

**Result:** Created as specified. Deviation **A** applied — `ObjectName` 128 / `Verb` 32 / `DatabaseName` 128 instead of 250 (see the divergence table at the top). The reason is documented in an XML comment on the entity. `IsDeny` has a doc comment recording that it is excluded from the unique index on purpose.

**Verify:** `dotnet build BE/AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj` → ✅ 0 errors

---

## Step 2 — EF `DbSet` + relationship + cascade delete + unique index

**Status:** ✅ DONE

**Goal:** Wire the entity into the context so migrations can be generated.

**File:** `BE/AutoApiEngine.Persistence/Context/ApplicationDbContext.cs` (update)

**Tasks:**
1. Add `public DbSet<ApiKeyPermission> ApiKeyPermissions { get; set; } = null!;` (next to `ApiKeys`, line ~41).
2. In `OnModelCreating` (mirror the existing `ApiKey` block, lines 80–83):
   ```csharp
   modelBuilder.Entity<ApiKeyPermission>()
       .HasOne(p => p.ApiKey)
       .WithMany()
       .HasForeignKey(p => p.ApiKeyId)
       .OnDelete(DeleteBehavior.Cascade);
   ```
   - **The cascade is required, not cosmetic.** EF Core's default for a required FK is `ClientSetNull`, which **throws** on delete when dependents exist — and `ApiKeyController` has `DELETE /api/keys/{id}`. Without it, deleting a key that has ever been granted a scope 500s at runtime. Cascade makes scopes die with the key. *This is a pre-existing gap in the design, not something wildcards introduce; settle it here.*
3. Unique index on the exact 5-tuple → keeps re-grant idempotent and is the **backstop** for an exact duplicate (plan §3.2):
   `HasIndex(p => new { p.ApiKeyId, p.ObjectName, p.Verb, p.WorkspaceId, p.DatabaseName }).IsUnique()`.
4. **`IsDeny` is deliberately NOT in the index.** That omission is the enforcement mechanism: `(k, Orders, GET, ws, db, deny)` and `(k, Orders, GET, ws, db, grant)` collide on the index, so they **cannot coexist** — no ambiguous cell that `DELETE` cannot resolve. Leave a comment in the code saying exactly that, or a later reader will "fix" it.
5. Note the `null` columns are fine in a `UNIQUE` index on SQL Server (see Step 3's verification), so no column-level workaround is needed.

**Result:** All four done. EF emitted `onDelete: ReferentialAction.Cascade` on its own (no hand-fix needed) and the index comment explains the `IsDeny` omission. Task 5's premise is superseded by divergence **B** — on MySQL the `null` columns are *not* protected by the index, which is why Step 4 gained `ExistsExactScopeAsync`.

**Verify:** `dotnet build` passes. → ✅

---

## Step 3 — Migration `AddApiKeyPermission` + apply

**Status:** ✅ DONE

**Goal:** DB schema change. This is a **normal committed migration** — not part of the gitignored `InitialCreate` pair (plan §10).

**Tasks:**
1. From `BE/AutoApiEngine.ApiServices/`:
   ```
   dotnet ef migrations add AddApiKeyPermission --startup-project .
   dotnet ef database update --startup-project .
   ```
   > The `--project ../AutoApiEngine.Persistence` form in the plan **fails** — see the note at the top of this file.
2. Inspect the generated migration: it must create table `ApiKeyPermissions` with the unique index from Step 2, `IsDeny` as `BIT NOT NULL DEFAULT 0`, and the cascade delete in the FK.
3. **Prove `NULL` uniqueness in the migration.** SQL Server's `UNIQUE` index treats `NULL` as equal to `NULL`, so `(k, NULL, NULL, W1, Db)` can exist only once per key/workspace/database — which is what we want, but it is *not* standard SQL `WHERE`-clause semantics, so verify rather than assume:
   - `INSERT` `(k, NULL, NULL, W1, Db)` → **succeeds**
   - `INSERT` the identical row again → **fails 2605 / 2627**
   - Repeat the pair for `(k, NULL, GET, …)` and `(k, Orders, NULL, …)`.
4. Commit it (do **not** add to `.gitignore`).

**Result:** Migration `20260926190635_AddApiKeyPermission` generated and applied; **committed** (not gitignored). Verified DDL on the dev MySQL:

```sql
CREATE TABLE `ApiKeyPermissions` (
  `Id` char(36) NOT NULL,
  `ApiKeyId` char(36) NOT NULL,
  `ObjectName` varchar(128) DEFAULT NULL,
  `Verb` varchar(32) DEFAULT NULL,
  `WorkspaceId` char(36) NOT NULL,
  `DatabaseName` varchar(128) NOT NULL,
  `IsDeny` tinyint(1) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_ApiKeyPermissions_ApiKeyId_ObjectName_Verb_WorkspaceId_Datab~`
    (`ApiKeyId`,`ObjectName`,`Verb`,`WorkspaceId`,`DatabaseName`),
  CONSTRAINT `FK_ApiKeyPermissions_ApiKeys_ApiKeyId` FOREIGN KEY (`ApiKeyId`)
    REFERENCES `ApiKeys` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
```

Task 3's live results on the dev container:
- all-`NULL` duplicate inserted twice → **both accepted** (divergence **B**; SQL Server would reject the second)
- non-`NULL` duplicate → second insert **`ERROR 1062 Duplicate entry`** → index backstop confirmed for the non-wildcard case
- cascade: deleting a scratch `ApiKey` removed its 3 permission rows (`3 → 0`); all scratch data cleaned up

`IsDeny` is `tinyint(1) NOT NULL` (MySQL's `bool`), not `BIT` — divergence **C**.

**Verify:** `dotnet build` passes. → ✅ Migration applies cleanly and the duplicate-`NULL` behaviour is documented rather than assumed.

---

## Step 4 — Repository interface + implementation

**Status:** ✅ DONE

**Goal:** Data-access layer for the filter and the permissions CRUD.

**Files:**
- `BE/AutoApiEngine.ServiceAbstraction/IApiKeyPermissionRepository.cs` (new)
- `BE/AutoApiEngine.Services/Repositories/ApiKeyPermissionRepository.cs` (new)

**Tasks:**
1. `public interface IApiKeyPermissionRepository : IGenericRepository<ApiKeyPermission>` (the generic lives in the misnamed `IGenricRepository.cs` — keep the convention, don't "fix" it).
2. Implement extending `GenericRepository<ApiKeyPermission>` (same shape as `ApiKeyRepository`).
3. Methods per plan §3.4:

   | Method | Used by |
   |---|---|
   | `GetInScopeAsync(objectName, verb, workspaceId, databaseName, ct)` — **replaces `GetByObjectAndScopeAsync`**. The whole predicate in one SQL query: `(r.ObjectName IS NULL OR r.ObjectName = @objectName) AND (r.Verb IS NULL OR r.Verb = @verb) AND (r.WorkspaceId = @workspaceId) AND (r.DatabaseName = @dbName)` | filter step 3 |
   | `ExistsDenyAsync(apiKeyId, objectName, string? verb, workspaceId, databaseName, ct)` — L1 when `verb` supplied, L2 when `verb` is `null` | filter step 5 |
   | `ExistsBroaderScopeAsync(apiKeyId, string? objectName, string? verb, workspaceId, databaseName, ct)` — the plan §5.3 redundancy check (grant-covered **and** deny-covered) | `ApiKeyPermissionController` create |
   | `GetByApiKeyIdAsync(apiKeyId, ct)` | `GET /api/keys/{id}/permissions` |
   | `GetByOrganizationAsync(orgId, ct)` — scope by `Workspace.OrganizationId` **as well as** `ApiKey.OrganizationId` (recovery path) | org-wide FE list / Scopes tab |
   | `FindActiveKeyByHashAsync(keyHash, ct)` | filter step 4 (may live on `IApiKeyRepository` — either placement is fine, but it must be injectable into the filter) |

4. **`GetInScopeAsync` must be a single query, not fetch-then-filter.** The old shape fetched with `databaseName: null` and filtered in C#; that cannot express the `IS NULL` wildcard branches, and a foreign-database wildcard row would then drive the control flow (a `SalesDb` row could make an `OrdersDb` request key-required). Pass `dbName` straight into the predicate.
5. `ObjectName` is compared **in SQL**, so case-sensitivity is now a **column-collation** question, not a C# question. If the dev DB is case-sensitive, `LOWER()` both sides or switch the CI collation to `Latin1_General_CI_AI` — decide and note it.
6. `IsDeny = true` with `ObjectName == null` is rejected here as well as in the controller (defence in depth, D12).

**Result:** All six methods implemented and exercised live against the dev MySQL with seeded data. Two additions beyond the plan:
- **`ExistsExactScopeAsync(apiKeyId, objectName, verb, workspaceId, databaseName, isDeny, ct)`** — the approved divergence **B** method. Compares `NULL`s explicitly so the 409 contract holds on MySQL as well as SQL Server; `IsDeny` is part of the test because the index deliberately excludes it.
- **`GetExplicitVerbGrantsAsync(apiKeyId, objectName, workspaceId, databaseName, ct)`** — returns the grant rows on one object that name a specific verb. Needed because `ExistsBroaderScopeAsync` **cannot** express the L2-deny-vs-explicit-grant check: with an incoming `verb = null` its `(p.Verb == null || p.Verb == verb)` branch collapses to `p.Verb == null`, matching only wildcard-verb grants. Returning rows (not a bool) lets the controller name the offending verb in the 400 message.

Task 5 resolved: collation is `utf8mb4_0900_ai_ci`, so comparisons are already case- and accent-insensitive — **no `LOWER()`**, which would only defeat the index. Noted in a comment at the comparison. Task 6's D12 guard is enforced in the controller; the repository methods are all `objectName`-driven and the L2 probe is only ever called with a non-null object.

**Verify:** `dotnet build` passes. → ✅

---

## Step 5 — DTOs

**Status:** ✅ DONE

**Goal:** Request/response shapes for the permissions API (plan §5.2).

**Files:**
- `BE/AutoApiEngine.ServiceAbstraction/DTO/CreateApiKeyPermissionDto.cs` (new)
- `BE/AutoApiEngine.ServiceAbstraction/DTO/ApiKeyPermissionDto.cs` (new)

**Tasks:**
1. `CreateApiKeyPermissionDto { string? ObjectName, string? Verb, Guid WorkspaceId, string? DatabaseName, bool IsDeny = false }` — `ObjectName`/`Verb` are nullable (a `null` is a wildcard, an empty string is **not**), `DatabaseName` optional (backend fills from `Workspace.DatabaseName` when omitted). **No `OrganizationId`** — see Step 7.
2. `ApiKeyPermissionDto { Id, ApiKeyId, ApiKeyName?, string? ObjectName, string? Verb, bool IsDeny, Guid WorkspaceId, string? WorkspaceName, string DatabaseName, CreatedAt }` — the FE list row; nullable object/verb round-trips the wildcard.

**Result:** Both created, matching the folder's existing class style. `CreateApiKeyPermissionDto` has **no `OrganizationId`** and an XML comment records why (a caller-supplied org id would be a cross-org grant waiting to happen).

**Verify:** `dotnet build` passes. → ✅

---

## Step 6 — `ApiKeyHasher` shared static, refactor `ApiKeyController` to use it

**Status:** ✅ DONE

**Goal:** Eliminate the hash-convention drift risk (plan §10: "or keys silently never match").

**Files:**
- `BE/AutoApiEngine.ServiceAbstraction/Common/ApiKeyHasher.cs` (new — placed next to `IGenricRepository.cs` in the `Common` folder)
- `BE/AutoApiEngine.Presentation/Controllers/ApiKeyController.cs` (refactor)

**Tasks:**
1. Move the current `HashKey` body (lines 157–162) into a shared static, preserving the exact output:
   ```csharp
   public static class ApiKeyHasher
   {
       public static string Hash(string plainKey) { /* SHA256 → Convert.ToHexString → ToLowerInvariant */ }
   }
   ```
2. Refactor `ApiKeyController.HashKey` → call `ApiKeyHasher.Hash(...)` (or delete the private method and call the shared one directly). **No behavior change** — existing keys must keep matching.

**Result:** Done. The private `HashKey` was deleted and `ApiKeyController.Create` now calls `ApiKeyHasher.Hash(plainKey)`; the now-unused `System.Security.Cryptography` / `System.Text` usings were dropped. `GenerateKey` stays private in the controller — only the hasher moved. **Output verified byte-identical** to the pre-extraction implementation, so existing keys keep matching. An XML comment records that the hex-lowercase format is load-bearing and must not change.

**Verify:** `dotnet build` passes. → ✅

---

## Step 7 — `ApiKeyPermissionController` (JWT-only CRUD)

**Status:** ✅ DONE

**Goal:** Management API for permissions (plan §5). **JWT-only** — runtime enforcement on dynamic endpoints is the filter's job, not this controller's.

**File:** `BE/AutoApiEngine.Presentation/Controllers/ApiKeyPermissionController.cs` (new)

**Tasks:**
1. `[ApiController] [Route("api/keys/{apiKeyId:guid}/permissions")] [Authorize]`, inheriting `BaseController` (use `HandleRequestAsync(...)` per BE convention).
2. Endpoints per plan §5.1:

   | Method | Route | Notes |
   |---|---|---|
   | GET | `/api/keys/{apiKeyId}/permissions` | rows for one key |
   | GET | `/api/permissions?organizationId={id}` | org-wide for the Scopes UI; each row carries `apiKeyId`, `apiKeyName`, `objectName`, `verb`, `isDeny`, `workspaceId`, `workspaceName`, `databaseName` |
   | POST | `/api/keys/{apiKeyId}/permissions` | body = `CreateApiKeyPermissionDto`; fill `DatabaseName` from `Workspace.DatabaseName` when omitted; **409** on an exact duplicate, **400** on a redundant-but-broader scope / deny conflict / invalid field, and **`403` when the workspace belongs to a different organization than the caller** (D10) |
   | DELETE | `/api/keys/{apiKeyId}/permissions/{permissionId}` | remove a row |

3. **Field validation** (plan §5.3):
   - `ObjectName` — `null` (wildcard) or a non-empty object name that exists in that workspace. **Empty or whitespace-only string → 400**: an empty string is **not** a wildcard. `"*"` is **not** accepted either — only `null` is (D2).
   - `Verb` — **`null` (wildcard)** or in `{ GET, POST, PUT, DELETE }` → else 400.
   - `DatabaseName` — filled from `Workspace.DatabaseName` when omitted; when supplied it must match the workspace's database → else 400.
   - `IsDeny = true` requires `ObjectName != null` → else 400 (D12).
4. **Only additive changes are allowed** (plan §5.3). Because deny always wins, a naive create can silently override a grant or a deny — so reject every non-additive change up front:

   | Change | Verdict | Message |
   |---|---|---|
   | Grant that adds access nothing else already grants | allow | — |
   | Grant already granted by an existing row of the same key | **400** | `"This scope is already covered by a broader grant: {scope}."` |
   | Grant already **denied** by an existing deny row | **400** | `"{ObjectName} is denied for this key. Remove the deny before granting."` |
   | Deny, nothing in conflict | allow | — |
   | Deny already covered by an existing deny row | **400** | `"This deny is already covered by a broader deny: {scope}."` |
   | Deny that would override an existing **explicit** grant on the same object | **400** | `"{ObjectName} already has an explicit {Verb} grant. Remove it before denying all verbs."` |

   Redundancy test — existing row `A` covers the incoming `B` (same key, workspace, database):
   ```
   (A.ObjectName IS NULL OR A.ObjectName = B.ObjectName)
   AND (A.Verb       IS NULL OR A.Verb       = B.Verb)
   ```
   A **deny row is never "covered" by a grant row** — the grant and deny checks are separate. Implement with `ExistsBroaderScopeAsync` (Step 4).
5. **Redundancy is semantic, not structural.** The unique index is on the exact 5-tuple, so `(k, NULL, GET, …)` and `(k, Orders, GET, …)` are *distinct legal rows* the database will happily accept. Do not try to encode this in the index; it lives here and in the repository only.
6. **Status codes, one source of truth** (the earlier plan contradicted itself on this — resolved): **409** for an exact duplicate (same 5-tuple, unique-index backstop), **400** for a redundant-but-broader scope, a deny conflict, and invalid fields, **403** for cross-org.
7. **Org containment (D10) — unchanged:**
   - `ApiKeyId` exists **and belongs to the caller's organization** (JWT `organization` claim — same pattern as `ApiKeyController.Create` lines 72–87).
   - `WorkspaceId` **exists and belongs to the caller's organization** (plan §5.3). Existence alone is not enough. Copy the pattern of the existing helper **`DynamicApiController.VerifyWorkspaceAccessAsync`** (`DynamicApiController.cs` lines 42–63): load via `IWorkspaceRepository.GetByIdWithOrganizationAsync(workspaceId, ct)` (Organization eagerly loaded) and compare `workspace.Organization?.Name` against the claim → mismatch is **403**. Missing/empty claim → **403**, as the helper does.
   - **The `organization` claim is an org Name, not an Id.** Alternative accepted (mirroring `ApiKeyController.Create` lines 79–87 / `WorkspaceController` lines 258–268): resolve the claim with `IOrganizationRepository.FindAsync(o => o.Name == orgName, ct)`, then compare `workspace.OrganizationId` to `organization.Id` — more robust if orgs can be renamed.
   - **Never trust an org id from the request.** `CreateApiKeyPermissionDto` has no `OrganizationId` and must not gain one — the org comes from the JWT claim only. (This deliberately differs from `ApiKeyController.Create`, which prefers `dto.OrganizationId`.)
   - All three must be the **same** org: route `{apiKeyId}` org, resolved `workspace.OrganizationId`, JWT claim org. Any pair mismatching → **403**.
   - Recovery path: `GetByOrganizationAsync` should scope by `Workspace.OrganizationId` **as well as** `ApiKey.OrganizationId`, so a cross-org row would at least be visible to the org owning the workspace.

**Result:** Created. All four endpoints, all four validation rules, all six additive-only rules, and the D10 containment checks are implemented. Notable choices, all commented in the code:
- **`HandleAsync<T>` instead of `HandleRequestAsync`.** The base helper maps only 404/400/500, so a 403 or 409 raised through it would surface as a **500**. A private sibling adds `OrgAccessDeniedException → 403` and `DuplicateScopeException → 409`, plus `DbUpdateException → 409` as the unique-index backstop. `BaseController` itself is untouched because every other controller depends on its current behaviour.
- **The claim-Name → Id route was chosen** (the plan's stated alternative) via `IOrganizationRepository.FindAsync`, then compared as a `Guid` thereafter.
- **Org containment is applied to all four actions, not just POST** — the plan only spelled it out for POST, but a caller must not be able to read or delete another org's rows. The org-wide GET additionally rejects a `?organizationId=` that is not the caller's own (403), so this new endpoint does not repeat the pre-existing `ApiKeyController.GetByOrganizationId` IDOR documented under Risks.
- **Ordering inside POST** is org (403) → field validation (400) → duplicate (409) → redundancy (400) → insert, so a cross-org caller learns nothing about which scopes exist.
- **`Verb` is upper-cased before storing**, since the filter compares against `Request.Method.ToUpperInvariant()`; a lower-case verb would create a row that never matches.
- **Object existence** is checked with `IDynamicApiMetadataService.GetObjectMetadataAsync`, which throws `ArgumentException` for a missing object → 400. This needs a live connection to the target database, so an unreachable target surfaces as a 500 rather than silently accepting an unverified name.
- **`{scope}` is rendered** as `All objects · GET` style labels so the messages read well in the UI.

**Verify:** `dotnet build` passes. → ✅ Runtime behaviour still owes the Step 10 matrix.

---

## Step 8 — DI registration

**Status:** ✅ DONE

**Goal:** Register the new repo + filter (plan §4.1 "ServiceFilter needs DI registration" + Step 8 table).

**File:** `BE/AutoApiEngine.ApiServices/Providers/ServiceCollectionExtensions.cs` (update)

**Tasks:**
1. `services.AddScoped<IApiKeyPermissionRepository, ApiKeyPermissionRepository>();`
2. `services.AddScoped<ApiKeyAuthFilter>();` (required for `[ServiceFilter]` on the controller actions)

**Result:** Both registrations added to `ServiceCollectionExtensions.cs` — `IApiKeyPermissionRepository` → `ApiKeyPermissionRepository` next to the existing `IApiKeyRepository` line, and `ApiKeyAuthFilter` as a scoped concrete (required because the actions reference it via `[ServiceFilter(typeof(ApiKeyAuthFilter))]`, which resolves the type from DI rather than instantiating it). A `using AutoApiEngine.Presentation.Filters;` was added.

**Verify:** `dotnet build` passes. → ✅

---

## Step 9 — `Filters/ApiKeyAuthFilter.cs` + attribute wiring

**Status:** ✅ DONE

**Goal:** The runtime gate on dynamic CRUD endpoints (plan §4). This is the core of the story.

**Files:**
- `BE/AutoApiEngine.Presentation/Filters/ApiKeyAuthFilter.cs` (new)
- `BE/AutoApiEngine.Presentation/Controllers/DynamicApiController.cs` (update)

**Tasks:**
1. `public class ApiKeyAuthFilter : IAsyncAuthorizationFilter` (authorization filters run before model binding → can short-circuit with `context.Result`).
2. CTOR-inject `IApiKeyPermissionRepository` (+ optional `IApiKeyRepository` for key lookup) and `IWorkspaceRepository`.
3. Algorithm (plan §4.2) — implement it **as written**, the order matters:

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
     //    The whole predicate is in SQL.
     rows = repo.GetInScopeAsync(objectName, verb, workspaceId, dbName)
     if rows.empty(): pass (fallback = ALLOW, D1 EXACT TUPLE — D13)

     // 4. Key required — UNCHANGED
     keyValue = Request.Headers["X-Api-Key"].FirstOrDefault()
     if empty → 401 API_KEY_REQUIRED
     apiKey = repo.FindActiveKeyByHashAsync(ApiKeyHasher.Hash(keyValue))
     if apiKey == null → 401 API_KEY_INVALID

     // 5. Deny wins, then union of grants
     if await repo.ExistsDenyAsync(apiKey.Id, objectName, verb,       workspaceId, dbName) → 403 API_KEY_DENIED   // L1
     if await repo.ExistsDenyAsync(apiKey.Id, objectName, verb: null, workspaceId, dbName) → 403 API_KEY_DENIED   // L2
     if !rows.Any(r => !r.IsDeny && r.ApiKeyId == apiKey.Id) → 403 API_KEY_FORBIDDEN

     // 5b. Org containment (D10) — UNCHANGED
     if apiKey.OrganizationId != workspace.OrganizationId → 403 API_KEY_WRONG_ORGANIZATION

     // 6. Allow + best-effort usage — UNCHANGED
     apiKey.LastUsedAt = UtcNow; fire-and-forget save
     pass
   ```

   Notes to carry into the code review:
   - **Grants accumulate.** Access is the union of all matching grant rows — there is no "more specific overrides broader" subtraction, and a specific row never revokes a broader grant.
   - **Two existence checks, no lattice.** Because `IsDeny` is capped to object-scoped, every deny is maximally specific (L1 = this object+verb, L2 = this object), so no specificity comparator is needed. Deny is checked before the grant union and always wins.
   - **The workspace lookup moved above the row query** because `dbName` is now part of the SQL predicate — there is no in-memory filtering step left to do it.
   - **`verb` = `Request.Method.ToUpperInvariant()`** — lines up with stored verbs for every object type (Table GET/POST/PUT/DELETE; View/Function GET; SP single GET or POST). `OPTIONS` preflights never match a verb row → always pass.
   - **Step 5b placement is deliberate:** after the 403s (so a rejected key never records `LastUsedAt`) and before step 6. No extra query — both `Guid`s are already in hand.
4. Wiring on `DynamicApiController`:
   - `[ServiceFilter(typeof(ApiKeyAuthFilter))]` on the **5 CRUD actions only** (GetList / GetById / Create / Update / Delete) — **not** on `GetObjectColumns`.
   - Add `[Authorize]` to `GetObjectColumns` (line 73 action) → **metadata becomes JWT-only** (plan §7). Keep controller-level `//[Authorize]` **commented out** so anonymous + `X-Api-Key` CRUD clients keep working (**D11** — deliberate, not an oversight; do not un-comment it in this story).
5. Response codes (plan §4.3): no row in scope → pass-through 200 · missing header → 401 `API_KEY_REQUIRED` · bad key → 401 `API_KEY_INVALID` · valid key + an L1/L2 deny matches → 403 `API_KEY_DENIED` · valid key, no deny, no grant of its own → 403 `API_KEY_FORBIDDEN` · valid key holding a grant but `apiKey.OrganizationId != workspace.OrganizationId` → 403 `API_KEY_WRONG_ORGANIZATION` (step 5b, D10).

**Result:** Implemented exactly in the order given — the order is load-bearing. Wiring verified on `DynamicApiController`: `[ServiceFilter(typeof(ApiKeyAuthFilter))]` sits on the **5 CRUD actions only** (GetList L132, GetById L235, Create L266, Update L330, Delete L361), `[Authorize]` is on the `GetObjectColumns` metadata action (L84), and the controller-level `//[Authorize]` stays **commented out** (L20, D11) with a comment explaining that un-commenting it would demand a JWT from every API-key client. Judgement calls, all commented in the code:
- **Unparseable route values and a missing workspace fall through as pass-through**, not as auth failures. The filter's job is authorization; a 404/400 is the action's, and inventing an auth error here would mask it.
- **A workspace with no `DatabaseName` also falls through.** `DatabaseName` is denormalized at grant time and `[Required]`, so such a workspace can never match a stored scope and the predicate would be empty anyway.
- **Both deny probes (L1 and L2) are combined into one `if`** with `||` short-circuiting, so the second query only runs when the first misses.
- **Rejections share a `{ code, message }` body** (`API_KEY_REQUIRED` / `API_KEY_INVALID` / `API_KEY_DENIED` / `API_KEY_FORBIDDEN` / `API_KEY_WRONG_ORGANIZATION`) so the FE can branch on `code` and show `message` verbatim.
- **`LastUsedAt` is written after every 403** — a rejected key never records usage. The write is wrapped in try/catch and swallowed, and needs an explicit `UpdateAsync` because `FindActiveKeyByHashAsync` returns the key `AsNoTracking`.
- **No claim is read anywhere in the filter (D11).** A comment records the trap explicitly: a conditional `if (claim != null) checkOrg()` is defeated by omitting the `Authorization` header, and `if (claim == null) → 403` breaks every API-key client. Org comes solely from `apiKey.OrganizationId` vs `workspace.OrganizationId`, zero extra queries.

**Verify:** `dotnet build` passes. → ✅ Runtime behaviour still owes the Step 10 matrix.

**Status:** ✅ DONE — build ✅ green, filter matrix ✅ **38/38**, controller matrix ✅ **11/11**. Both bugs found by running it are fixed (Bug 1 in this story, Bug 2 pre-existing in `Program.cs`).

**Goal:** Green build before moving to the frontend (plan Step 10).

**Result (2026-09-27):**

`dotnet build` → **0 errors**, only the 2 pre-existing `CS8604` warnings
(`DesignTimeDbContextFactory.cs:22`, `Providers/DataBaseProvider.cs:13`).

The backend was run for real and driven against the plan §9 matrix: **38/38 filter checks** and
**11/11 Step 7 controller checks** pass. Running it is what surfaced **two genuine bugs**, which is
exactly why this step exists: *"Verify: dotnet build passes"* is not verification. Both are now
fixed — see **Bug 1** and **Bug 2** below.

---

### 🐞 Bug 1 (this story) — `GET` permission endpoints threw 500 — **FIXED**

`GET /api/keys/{id}/permissions` and `GET /api/permissions?organizationId=…` both returned:

```
500  "details": "Expression '@workspaceIds' in the SQL tree does not have a type mapping assigned."
```

**Cause.** `ProjectAsync` resolved workspace names with
`_workspaceRepository.FindAsync(w => workspaceIds.Contains(w.Id))`. `Guid` is stored as `char(36)`, and
the MySQL provider **cannot resolve a type mapping for a `Guid` *collection* parameter**. Scalar
equality (`w.Id == guid`) translates fine — which is why `GetByIdWithOrganizationAsync` works and only
the `Contains` form failed.

**Fix.** Added `IWorkspaceRepository.GetByIdsAsync`, which builds the predicate as an OR-chain of
scalar `Guid` equalities (`w => w.Id == id1 || w.Id == id2 || …`). Still one server-side round-trip, no
N+1, no client evaluation, works on both providers. The reason is documented at the implementation so
nobody "simplifies" it back to `Contains()` — it compiles fine and fails only at runtime.

---

### 🐞 Bug 2 (PRE-EXISTING, not this story) — every `[Authorize]` endpoint returned `200` with an empty body — **ROOT-CAUSED & FIXED**

This one was **not** caused by this story. It reproduced on controllers nobody here touched
(`ApiKeyController`, `WorkspaceController`, `SchemaExplorerController`).

**Symptom (as originally observed).** With a token in the `Authorization` header, every `[Authorize]`
action answered `200 OK`, `Content-Length: 0`, **no** `Content-Type`, and the action never executed.

**Root cause — two independent facts that had to meet.**

1. **The trigger: token validation was failing on every request.** `options.Authority` is
   `http://localhost:8081/realms/ApiEngineRealm`. **Keycloak was not running** during the original
   verification (`docker ps` showed only `mysql`), so the handler could not fetch the OIDC discovery
   document and *every* token failed validation. Consequence: the "valid JWT" used in that run was
   never actually valid — a `client_credentials` token minted against a reachable Keycloak cannot
   validate while Keycloak is down.
2. **The defect: `Program.cs` swallowed the resulting 401.** In `JwtBearerEvents.OnChallenge`:

   ```csharp
   if (context.AuthenticateFailure != null)
   {
       context.HandleResponse();   // "suppress your default 401"
   }
   return Task.CompletedTask;     // ...and then writes nothing in its place
   ```

   `HandleResponse()` tells the framework *"I am taking over the challenge response."* The handler
   accepts that responsibility and never exercises it. **No branch in that lambda can ever write
   anything**, so nothing ever touches the response and Kestrel finalises its untouched default:
   **`200`, zero bytes, no `Content-Type`.** The 401 was not sent with an empty body — it was
   discarded entirely.

**Why the symptom looked so strange — the fingerprint.** The bug only fires when a token is *present
and invalid*. With **no** `Authorization` header the handler short-circuits to `NoResult()`, never
contacts the authority, leaves `AuthenticateFailure == null`, so the `if` never runs and the
framework's own 401 is written normally. That is why every "no token → 401" row of the matrix looked
healthy while every "with token" row broke:

| Sent `Authorization`? | Token valid? | Result |
|---|---|---|
| No | — | **401** + `WWW-Authenticate` — handler short-circuits, `AuthenticateFailure` stays `null` |
| Yes | ✅ | **200** + real body — no challenge is ever issued |
| Yes | ❌ | **200, 0 bytes, no `Content-Type`** ← the bug |

Not the cause, all ruled out: `Program.cs` pipeline (plain — `UseCors` → `UseAuthentication` →
`UseAuthorization` → `MapControllers`, no custom middleware, no global result filter, no output
formatter override, `Ok` is not shadowed), **HTTP vs HTTPS** (byte-identical on `:5145` and `:7002`),
`Accept` header (`*/*`, `application/json`, `text/plain` all identical), and content negotiation.

**Proof.** Logging inside `OnChallenge` gave the smoking gun, and it also proved the token was
failing rather than the action misbehaving:

```
[DIAG] OnChallenge fired. path=/api/workspaces hasAuthHeader=False authFailure=<null>
[DIAG] OnChallenge fired. path=/api/workspaces hasAuthHeader=True
       authFailure=ArgumentException: IDX14102: Unable to decode the header ...
```

The valid-token request produced **no `OnChallenge` line at all** — no challenge, action ran, 1748
bytes returned. Absence of the log line *is* the proof that the action executed.

**Fix** (`Program.cs`, deliberately a **separate, clearly-labelled change** — it is not part of this
story's feature work):

- `OnChallenge` now **takes over the challenge and actually writes it**: `401` + a JSON
  `{ code: "UNAUTHORIZED", message: "..." }` body when `AuthenticateFailure != null`. It still
  **returns early when `AuthenticateFailure == null`**, so the no-token case keeps the framework
  default and the `WWW-Authenticate` challenge header is still emitted.
- `OnForbidden` was **added** — the token was valid but the policy/claim check failed, which
  previously produced a bare bodyless `403`. Now a JSON `403` body.
- Both handlers guard on `context.Response.HasStarted` so a late challenge cannot corrupt a response
  already on the wire.

> ⚠️ **The code fix was missing on 2026-09-27 and has been restored.** This section was written at
> 13:49 on 2026-09-27, but `git diff` at that point showed `Program.cs` still at `HEAD` with the
> defective `HandleResponse()` handler — the write had been lost, so the doc described a fix that was
> not actually in the tree. The handler above has since been re-applied and re-verified (build 0 errors;
> garbage token → `401` + JSON body; valid token → `200` + 1748 bytes; no token → `401` +
> `WWW-Authenticate`; CORS preflight → `204`; anonymous dynamic CRUD and the JWT-only metadata
> endpoint both unaffected). **If you ever read "FIXED" in this doc, confirm the `Program.cs` diff is
> actually present before trusting it** — a doc-only fix is indistinguishable from a real one here.

**Verified after the fix** (`dotnet build` 0 errors; the only 2 warnings are the pre-existing
`CS8604`s):

| Request | Before | After |
|---|---|---|
| `[Authorize]`, **garbage** token | `200`, 0 bytes, no `Content-Type` | **`401`** + `{"code":"UNAUTHORIZED","message":"Invalid, expired or inactive access token."}` |
| `[Authorize]`, **tampered** payload | `200`, 0 bytes | **`401`** + same body |
| `[Authorize]`, **valid** token | `200` + 1748 bytes | **`200` + 1748 bytes** — unchanged, no regression |
| `[Authorize]`, **no** token | `401` | **`401`** + `WWW-Authenticate` — unchanged, no regression |
| `GET /api/permissions?organizationId=…` (this story) | — | **`403`** `ORG_ACCESS_DENIED` (Step 7's own guard) |
| `GET /api/keys/organization/…` | — | **`200`** `[]` |
| `GET /api/keys/{id}/permissions` (this story) | — | **`404`** with a proper message |

**Impact on this story: none of the feature code was affected.** `DynamicApiController` keeps
`//[Authorize]` commented out (D11), so the `X-Api-Key` path never enters the JWT pipeline — the
filter and its 38/38 matrix never touched this. The only endpoints of this story that reached the
JWT pipeline were the two `[Authorize]` management `GET`s from plan §5, and those now work.

---

### ✅ Filter matrix — 38/38 passing

Run against a live backend on `http://localhost:5145`, dev **MySQL** (`DbPortal` @ `localhost:3307`),
using workspace `ecommerce-db` (`99ce6ed6-…`, database `ecommerce-db`, object `employee`) and two
seeded keys. Every row seeds and cleans up its own rows; nothing was left behind.

| # | Scenario | Expect | Result |
|---|---|---|---|
| 1 | no row, no key, anonymous | 200 (D1/D13) | ✅ 200 |
| 2 | row exists, no `X-Api-Key` | 401 `API_KEY_REQUIRED` | ✅ |
| 3 | row exists, garbage key | 401 `API_KEY_INVALID` | ✅ |
| 4 | row for k-a, valid key k-b | 403 `API_KEY_FORBIDDEN` | ✅ |
| 5 | row for k-a, key k-a | 200 + `LastUsedAt` stamped | ✅ |
| 6 | only a GET row, caller sends POST | **not gated** (D13) | ✅ reached the action (`400` from its own body validation — which is the proof the filter let it through) |
| 7 | row `(employee, NULL)` | 401 on GET/POST/PUT/DELETE | ✅ ×4 |
| 8 | row `(NULL, GET)` | 401 on GET; POST not gated | ✅ |
| 9 | row `(NULL, NULL)` k-a only | k-a 200 / k-b 403 / anon 401 | ✅ ×3 |
| 10 | row `(NULL, NULL)` for both keys | both 200 | ✅ ×2 |
| 11 | L1 deny `(employee, GET)` | 403 `API_KEY_DENIED`; POST not gated | ✅ |
| 12 | grant + deny at the same **all-`NULL`** 5-tuple | `ERROR 1062` — the index is the backstop | ✅ |
| 13 | grant + deny at the same **`Verb IS NULL`** tuple | both rows insert (MySQL), **deny still wins** → 403 | ✅ |
| 14 | L2 deny `(employee, NULL)` | 403 `API_KEY_DENIED` on all four verbs | ✅ ×4 |
| 15 | `(NULL, GET, grant)` + `(Invoices, GET, deny)`, grant deleted | 403 — deny survives narrowing | ✅ |
| 16 | deny only, anonymous | 401 `API_KEY_REQUIRED` (the deny is itself a gating row) | ✅ |
| 17 | row on a different workspace/database | pass-through 200 | ✅ |
| 18 | expired key with a valid row | 401 `API_KEY_INVALID` | ✅ |
| 19 | inactive key with a valid row | 401 `API_KEY_INVALID` | ✅ |
| 20 | key of org B on org A's workspace | 403 `API_KEY_WRONG_ORGANIZATION` | ✅ (needed a second org, created one temporarily) |
| 21 | same row, key moved to org A | 200 — the check is not over-blocking | ✅ |
| 22 | `api/schema/{ws}/columns` without JWT | 401 (JWT-only) | ✅ |
| 23 | CORS preflight on a gated endpoint | 204 + `ACAO` | ✅ |
| 24 | Step 2 cascade: delete an `ApiKeys` row | its `ApiKeyPermission` rows go with it (1 → 0) | ✅ |

**Two notes on the plan's matrix, both now settled:**

- **Row 12/13 resolve the `IsDeny` question raised in D12.** A grant and a deny **cannot** coexist at
  the same all-non-`NULL` 5-tuple — the unique index excludes `IsDeny` precisely so they collide
  (`ERROR 1062`, verified). A deny therefore *replaces* a grant, which is why the controller refuses to
  create either over the other. The one gap is a `NULL`-bearing tuple, where MySQL treats the two rows
  as distinct; **deny still wins** (verified, row 13), and the controller's explicit checks are the
  guard. So the invariant holds on both providers.
- **Row 6/8/11 assert "not gated" rather than a specific success code.** A `400` from the action's own
  validation is *positive* evidence the filter passed, so asserting `status ∉ {401, 403}` is the
  correct assertion. Asserting a literal `200` would be wrong.

### ✅ Controller matrix — Step 7 validation, 11/11 passing

Run against a live backend on `http://localhost:5145` with a real JWT (Keycloak `client_credentials`
on `api-engine-service`; the service account's `org` user attribute is `org`, **not**
`organization` — the realm's `org-mapper` maps `org` → the `organization` claim). Org
`a107228c-…` ("Test Organization"), workspace `99ce6ed6-…` (`ecommerce-db` / database `ecommerce-db`),
key `2e71e9d4-…` ("API KEY TEST1"), object `employee`. Every row cleaned up after itself;
`ApiKeyPermissions` ended at 0 rows and no scratch org/workspace survived.

| # | Scenario | Expect | Result |
|---|---|---|---|
| C1 | `isDeny: true` with `objectName: null` (D12) | **400** | ✅ `"A deny must name a specific object, so ObjectName is r…"` |
| C2 | `objectName: ""` — empty is **not** a wildcard | **400** | ✅ |
| C3 | `objectName: "*"` — only `null` is the wildcard | **400** | ✅ `"\"*\" is not a wildcard…"` |
| C4 | `verb: "PATCH"` — not in `GET/POST/PUT/DELETE` | **400** | ✅ |
| C5 | `databaseName` not matching the workspace's | **400** | ✅ `"…must match the workspace's database ('eco…"` |
| C6 | baseline grant `(employee, NULL)` — the happy path | **2xx** | ✅ row created |
| C7 | re-POST the **identical 5-tuple** | **409** | ✅ `DUPLICATE_SCOPE` |
| C8 | grant over an existing **L1 deny** | **400** | ✅ `"employee is denied for this key. Remove the deny before gra…"` |
| C9 | **L1 deny** `(employee, GET)` under an existing **broader L2 deny** | **400** | ✅ `"This deny is already covered by a broader deny: employee · GET."` |
| C10 | **L2 deny** `(employee, NULL)` over an existing **explicit `GET` grant** | **400** | ✅ `"employee already has an explicit GET grant. Remove it be…"` |
| C11 | grant on **another org's** workspace (temp org + workspace) | **403** | ✅ `ORG_ACCESS_DENIED`, **no row created** |

**Three rows that look like failures but are the code being right** — recorded because the naive
expectation is wrong each time, and a future reader will otherwise "fix" them:

- **C7 vs C9 — `409` beats `400` when the 5-tuple is identical.** An L2 deny `(employee, NULL, deny)`
  posted over an existing L2 deny `(employee, NULL, grant)` is an *exact 5-tuple duplicate*, so
  `ExistsExactScopeAsync` (divergence **B**) fires first and returns **409**, not the 400
  "covered by a broader deny". The 400 branch is for a **strictly narrower** incoming deny — that is
  C9, where `(employee, GET, deny)` arrives under `(employee, NULL, deny)`. Ordering duplicate-before-
  redundancy is intentional (see Step 7 result: org → fields → duplicate → redundancy).
- **C6 vs C8 — a grant over a *deny* is the 400 "is denied" branch, not a duplicate.** `IsDeny` is
  excluded from the unique index precisely so grant and deny can collide there; the controller
  rejects it semantically with the readable message.
- **C11 — a workspace in the caller's *own* org returns 200.** An earlier attempt at this row used
  `spectrumdb`, which belongs to the same org, so `200` was correct. The 403 only appears with a
  genuinely foreign org.

**Note on the "not found" trap when hand-seeding a workspace.** `Workspaces` has five `NOT NULL`
columns with no default — `DatabaseEngine`, `IsActive`, `TablesCount`, `FunctionsCount`,
`StoredProceduresCount`. MySQL 8 strict mode **rejects** an insert that omits them, and the row is
never created, so the endpoint then correctly answers `400 "Workspace with id … not found"` — which
reads exactly like an application bug. Include all five when seeding a scratch workspace.

---

**Tasks:**

1. `dotnet build BE/AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj` → 0 errors. ✅
2. Manual smoke on a running backend — plan §9 matrix: filter rows ✅ **38/38**; controller rows ✅ **11/11**.

   | Scenario | Expect |
   |---|---|
   | No permission row, no key, anonymous | **200** (D1/D13 fallback) |
   | Row exists (obj+GET+ws+db), no `X-Api-Key` | **401 `API_KEY_REQUIRED`** |
   | Row exists, garbage key | **401 `API_KEY_INVALID`** |
   | Row exists, valid key that lacks the row | **403 `API_KEY_FORBIDDEN`** |
   | Row exists, valid key that holds the row | **200**; `LastUsedAt` updated |
   | Only a `GET` row; caller sends `POST` | **200** — no `POST` row in scope, D1 exact-tuple fallback. **Not a bug, do not "fix"** (D13) |
   | Row `(Orders, NULL)`; `GET` / `POST` / `DELETE` | **401** each |
   | Row `(NULL, GET)`; `GET` on any object | **401** |
   | Row `(NULL, NULL)` for `k-a` only: with `k-a` / with `k-b` / anonymous | **200** / **403 `API_KEY_FORBIDDEN`** / **401 `API_KEY_REQUIRED`** |
   | Row `(NULL, NULL)` for `k-a` **and** `k-b`: with either | **200** both |
   | Row `(Orders, GET, deny)`; `GET /Orders` with `k-a` | **403 `API_KEY_DENIED`** |
   | Row `(Orders, NULL, deny)` (L2) on all four verbs | **403 `API_KEY_DENIED`** each |
   | `(NULL, GET, grant)` + `(Invoices, GET, deny)`; delete the grant; `GET /Invoices` | **403 `API_KEY_DENIED`** — deny survives narrowing |
   | Deny added to an object with no other grant; anonymous | **401 `API_KEY_REQUIRED`** (the deny is itself a gating row) |
   | SP classified POST → HTTP POST + key | **200** routine executes |
   | Row targets a different workspace/database | pass-through **200** |
   | Expired / inactive key with a valid row | **401 `API_KEY_INVALID`** |
   | `OPTIONS` preflight on a gated endpoint | passes |
   | `api/schema/{ws}/columns` without JWT | **401** (JWT-only) |
   | `api/schema/{ws}/columns` with JWT | **200** unchanged |
   | Duplicate grant via API (exact 5-tuple) | **409** |
   | Grant already covered by a broader grant of the same key | **400** "already covered by a broader grant" |
   | Grant already denied | **400** "is denied for this key" |
   | Deny already covered by a broader deny | **400** "already covered by a broader deny" |
   | L2 deny over an existing explicit grant on that object | **400** "already has an explicit … grant" |
   | `IsDeny = true`, `ObjectName = null` | **400** |
   | `ObjectName = ""` and `ObjectName = "*"` | **400** (only `null` is the wildcard) |
   | Grant attempt: caller org A, `workspaceId` of org B's workspace | **403** (rejected at plan §5.3, no row created) |
   | Row seeded directly in DB: key of org A on org B's workspace + valid `X-Api-Key` | **403 `API_KEY_WRONG_ORGANIZATION`** (filter step 5b) |
   | Org A grant on org A's workspace | **200/201** unchanged |
   | Org A and Org B both hold `Orders/GET` on their own workspaces | both **200** independently; rows coexist (unique index includes `ApiKeyId`) |
   | Migration: identical `(k, NULL, NULL, W1, Db)` inserted twice | first **succeeds**, second fails **2605/2627** — ⚠️ **on MySQL the second SUCCEEDS**; the 409 is now enforced in the controller instead (divergence **B**) |

3. Existing `api/keys` page regression → still works (controller untouched, only `HashKey` extraction). **Also** verify `DELETE /api/keys/{id}` on a key that holds scopes — this is what the Step 2 cascade exists for.

---

## Step 11 — Frontend: Scopes service (DTOs + permission CRUD)

**Status:** ✅ DONE — build ✅ green, all five endpoints exercised live (see the Step 12 verification block)

**Goal:** FE can read org keys/permissions and grant/revoke scopes. **D8 reversed** — this is a new co-located service on the **api-keys** page, not an extension of `dynamic-api.service.ts` (plan §6.1).

**File:** `FE/src/app/dashboard/api-keys/api-key-scopes.service.ts` (new)

**Tasks:**
1. Follow the repo's co-location convention `feature/feature.ts|.html|.scss` and its `inject()` preference. Do **not** modify `dynamic-api.service.ts` to hold permission CRUD.
2. Interfaces:
   ```ts
   interface ApiKeySummary { id: string; name: string; isActive: boolean; expiresAt: string | null }
   interface ApiKeyPermissionDto { id: string; apiKeyId: string; apiKeyName?: string;
     objectName: string | null; verb: string | null; isDeny: boolean; workspaceId: string;
     workspaceName?: string; databaseName: string; createdAt: string }
   ```
3. Methods (all `withCredentials: true`):

   | Method | Call |
   |---|---|
   | `getOrganizationKeys(orgId)` | `GET {api}/keys/organization/{orgId}` (existing `ApiKeyController` endpoint) |
   | `listPermissions(apiKeyId)` | `GET {api}/keys/{apiKeyId}/permissions` |
   | `listOrgPermissions(orgId)` | `GET {api}/permissions?organizationId={orgId}` |
   | `createPermission(apiKeyId, body)` | `POST {api}/keys/{apiKeyId}/permissions` |
   | `deletePermission(apiKeyId, permissionId)` | `DELETE {api}/keys/{apiKeyId}/permissions/{permissionId}` |

4. **No new backend endpoint for object enumeration.** The component (Step 12) injects the existing `DynamicApiService` and calls `exploreSchema(workspaceId)`, which already returns `objects: SchemaObject[]` typed `'Table' | 'View' | 'StoredProcedure' | 'Function'` (`dynamic-api.service.ts:8–21`, method line 109). **Reuse it, do not move or duplicate it.**
5. Surface the server's `400` / `409` / `403` message bodies as-is — they are written to be user-readable (plan §5.3).

**Verify:** `npm run build` from `FE/`.

---

## Step 12 — Frontend: Scopes grid state + deny/broad-scope logic

**Status:** ✅ DONE — build ✅ green. Required one additive **backend** change; see the "StoredProcedure verb" note below.

**Goal:** Component state and the grant/revoke/deny logic for the Scopes tab (plan §6.2–§6.3). **Per-key drill-down**, not an all-keys toggle matrix.

**File:** `FE/src/app/dashboard/api-keys/api-key-scopes.ts` (new, co-located)

**Tasks:**
1. New signals: `activeTab`, `orgKeys`, `orgPermissions`, `schemaObjects`, `selectedWorkspaceId`, `selectedKeyId`, `permissionBusy`, `permissionError`, `actionMessage`.
2. **Two pickers, in this order.** `selectedWorkspaceId` must be chosen before `selectedKeyId` and before the grid renders: the workspace determines `DatabaseName` and supplies the object list. Show the resolved scope as a read-only header, e.g. `Reporting-Key · Sales → SalesDb`.
3. Grid state contract — five symbols, and the mapping is what makes the UI honest:

   | Symbol | Meaning | Row it maps to | Clickable |
   |---|---|---|---|
   | `■ ON` | explicit **grant** row for exactly this object + verb | exactly one | yes → DELETE |
   | `⊘ DENIED` | explicit **deny** row (L1 for this verb, or an L2 covering the whole object) | exactly one, or one L2 for the whole row | yes → DELETE the deny |
   | `░` | no row of its own; granted by a **broader** row — tooltip *"Granted by: All objects · GET"* | none | **no** |
   | `□` | not granted | none | yes → POST |
   | `n/a` | the verb does not exist for this object type | none | no |

   > **Invariant: every `■` and every `⊘` maps to exactly one database row.** Toggle-off is therefore unambiguous, which is why `░` exists as a distinct state rather than rendering `■` and lying about which row is responsible.

4. An **L2 deny renders the whole object row `⊘`** with a single "remove deny" action, because that cell set maps to exactly one row — this is what keeps the invariant intact at object level.
5. A **`■` cell may drop to `░`** after its row is deleted while a broader row still covers it. That `■` → `░` transition is the **visible signal that a broader row took over** — expected behaviour; do not "correct" it in the component.
6. Verb columns by object type (must match filter §4.2): `Table` → `GET`/`POST`/`PUT`/`DELETE`; `View` and `Function` → `GET` only; `StoredProcedure` → `[meta.verb]` (single). `n/a` cells are disabled — no HTTP verb reaches them, so a grant on them could never be exercised.

   > #### 🐞 Deviation — StoredProcedure verb had to be added to the schema endpoint
   >
   > Step 11 task 4 assumed `exploreSchema()` was sufficient to drive this row. It is not: the
   > response carried only `name` / `type` / `columnCount`, with **no per-object `verb`**. The
   > classification lives in `DynamicApiMetadataService` (`:100–108`), which is reached only via
   > `GET /api/schema/{id}/columns?table=…` — one HTTP call per object, each opening its own DB
   > connection. Left as-is, `verbApplies()` compared against `undefined` and **every stored
   > procedure rendered four `n/a` cells**, i.e. no scope on an SP could ever be granted from the UI.
   >
   > **Fix (additive, no new endpoint):** `SchemaObjectDto.Verb` (`string?`) added, populated inside
   > the routines loop that already runs:
   > - `MySqlSchemaExplorer` — `ROUTINE_DEFINITION` added as a third column to the **existing**
   >   `information_schema.routines` query, so it rides along in the same round-trip.
   > - `SqlSchemaExplorer` — `LEFT JOIN sys.sql_modules m ON m.object_id = OBJECT_ID(r.ROUTINE_SCHEMA + '.' + r.SPECIFIC_NAME)`
   >   added to the existing `INFORMATION_SCHEMA.ROUTINES` query for the same reason.
   >
   > Both call the **already-registered** `SpVerbClassifier` (same project, so only a constructor
   > param on the two explorers) with the identical `Evaluate(definition) == SpVerb.Get ? "GET" : "POST"`
   > expression the metadata endpoint uses — the two can therefore never disagree. `View`/`Function`
   > are hardcoded `"GET"`, matching the metadata table. `Table` stays `null` (all four verbs reach it).
   > An unreadable definition (encrypted module) yields `""` → `POST`, the same fallback
   > `GetObjectMetadataAsync` already had, so this introduces no new failure mode.
   >
   > `SchemaObject` in `dynamic-api.service.ts` gained the matching optional `verb?: 'GET' | 'POST'`.
   > The two `(o as any).verb` casts in `api-key-scopes.ts` are now typed reads. The `api-generated`
   > page ignores the new field.
   >
   > **Verified:** `ecommerce-db` → `sp_createEmployee` = **POST**, the other five SPs = **GET**, all
   > 4 tables = `null`; cross-checked **6/6** against `/columns`, and independently corroborated by
   > the controller itself (`POST sp_allEmployee` → `405 "only supports GET"`).
7. Mutations: `□` → `createPermission(selectedKeyId, { objectName, verb, workspaceId })` (backend fills `databaseName`; add `isDeny: true` for a deny, which always sends a non-null `objectName`); `■`/`⊘` → `deletePermission(...)` of **the one** row behind the cell. Refresh `orgPermissions` after every mutation.
8. Disable `□` on an object that holds an L2 deny (the server would reject it with "is denied for this key"); hint the admin to remove the deny first.

**Verify:** `npm run build` from `FE/`. → ✅ 0 errors, 15 prerendered routes.

**Runtime verification of the whole Scopes loop (against a live backend + MySQL):** the grid is
pure view logic over rows the Scopes service already reads, so what actually needed proving is that
the rows the UI writes are the rows the filter enforces. Driven end-to-end with a real key:

| # | Scenario | Result |
|---|---|---|
| 1 | `GET sp_get_all_employees`, a row exists in scope, no key | **401 `API_KEY_REQUIRED`** — gated, key demanded |
| 2 | same, valid key with no grant of its own | **403 `API_KEY_FORBIDDEN`** — the pre-existing `GET` grant belongs to another key |
| 3 | same, garbage key | **401 `API_KEY_INVALID`** |
| 4 | `POST sp_createEmployee` **before** any grant on it | **400 `MISSING_PARAMETER`** — *not* 401, i.e. ungated and open (D1/D13 exact-tuple fallback) |
| 5 | grant `sp_createEmployee`/`POST`, then same call with no key | **401 `API_KEY_REQUIRED`** — now gated |
| 6 | same call **with** the granted key | **400 `MISSING_PARAMETER`** — passed the filter and reached the action, which is the proof the grant matched |
| 7 | try to add an L1 deny on top of that grant | **rejected `400`** by the Step 7 validation; row not created and #6 unchanged |
| 8 | add L2 deny `sp_get_all_employees` (verb `null`), then GET with the key | **403 `API_KEY_DENIED`** — deny precedence holds |
| 9 | `POST sp_allEmployee` (GET-classified) | **405 `"only supports GET"`** — independently confirms the schema endpoint's `GET` classification |

All rows created for this check were deleted afterwards; `ApiKeyPermissions` is back to empty.

---

## Step 13 — Frontend template: Scopes tab, grid, and the mandatory warnings

**Status:** ✅ DONE — build ✅ green

**Goal:** The visible UI (plan §6.3–§6.4), plus the api-generated page cleanup.

**Files:**
- `FE/src/app/dashboard/api-keys/api-key-scopes.html` (new)
- `FE/src/app/dashboard/api-keys/api-key-scopes.scss` (new)
- `FE/src/app/dashboard/api-keys/api-keys.html` (update — add the tab strip, wire the Scopes tab)
- `FE/src/app/dashboard/api-generated/api-generated.html` / `.scss` (update — **removal only**)

**Tasks:**
1. **Tab strip** on the API Keys page: existing keys view + new **"Scopes"** tab. `api-keys.ts` stays the key CRUD owner; the Scopes tab is its own component.
2. **Scopes tab layout**: workspace picker → key picker → the `■ ⊘ ░ □ n/a` grid (Objects down the left, `GET`/`POST`/`PUT`/`DELETE` across, with the `◇ All objects` wildcard row) → read-only `databaseName` line → error/success message lines driven by the server messages.
3. **Mandatory warning before removing a broad scope.** Because the D1 fallback is the exact tuple, deleting a broad scope **re-opens** everything it covered. Confirm first:
   > **Remove "All objects · GET"?**
   > Every object in `SalesDb` with no explicit scope becomes open to any caller — requests without a key will succeed. Objects with a `⊘` deny stay locked. Affected: **Invoices**, **OrderSummary**, and any table added later.
   > *You will need to grant each remaining object explicitly.*

   The affected-object list must be computed from the actual data, and the text must also cover the other workspaces the key reached through that row (see Risks) — not just the objects visible in the current grid.
4. **Mandatory warning when adding a deny to an object that has no other grant**: it turns a 200-for-everyone endpoint into a 401-for-everyone, for all keys and anonymous callers. Per-key action, global effect — say so.
5. **api-generated page: remove the "API Key Access" card** (permission CRUD moved). Keep the generated endpoints and the **Copy cURL** buttons exactly as they are — `curl -X <METHOD> "<url>" -H "X-Api-Key: YOUR_API_KEY_HERE"` plus `-H "Content-Type: application/json" -d '<bodyTemplate>'` for table/SP-POST, header only for GET, placeholder `YOUR_API_KEY_HERE` (plaintext keys are never retrievable — shown once at creation). **D5 unchanged.**

   > **Two deviations from the plan's premise here**, both because the page did not match what D5 assumed:
   >
   > - **No "API Key Access" card exists** on `api-generated.html` — a search of the template and its
   >   TS found nothing to remove. The "removal only" instruction is therefore satisfied as a **no-op**;
   >   nothing was deleted.
   > - **D5 said to keep the Copy cURL buttons "exactly as they are", but there were none** — only a
   >   bare *Copy URL* per endpoint. Since D5's own contract is a specific command (method + URL +
   >   `X-Api-Key` header + conditional body), it was **implemented as new**: `buildCurl()` /
   >   `copyCurl()` on `api-generated.ts`, wired into the endpoint cards with a preview block, plus
   >   the **routine-only** action row (`generatedUrls()[0]`) which has its own button group and would
   >   otherwise have been left with *Copy URL* alone.

**Verify:** `npm run build` from `FE/`. → ✅ 0 errors, 15 prerendered routes.

---

## Step 14 — Frontend verification

**Status:** 🚧 IN PROGRESS — build ✅ green (0 errors, 15 prerendered routes) and the data layer is
✅ proven end-to-end. What is **not** done is the click-through in a real browser, which needs a
signed-in user (Keycloak's `api-engine-app` client is confidential and uses `client-jwt`, so it
cannot be driven by a scripted password grant — see the note at the end).

**Goal:** Green FE build + interaction smoke (plan §9).

**Tasks:**
1. `npm run build` from `FE/` → 0 errors. → ✅
2. Manual (JWT-authenticated dashboard):
    - [ ] Workspace picker then key picker; the grid only renders once both are chosen, and the header shows the resolved `DatabaseName`
    - [ ] Toggle `□` → `■` on a table cell → row appears; toggle `■` → row removed
    - [ ] A cell drops from `■` to `░` after its row is deleted while a broader row still covers it (expected transition)
    - [ ] Add an L1 deny → cell shows `⊘`; add an L2 deny → the whole object row shows `⊘` with one "remove deny" action
    - [ ] Removing "All objects · GET" raises the warning dialog naming the affected objects and the re-open consequence
    - [ ] Adding a deny to an ungranted object raises the "turns this endpoint key-required for everyone" warning
    - [ ] Redundant grant surfaces the server's 400 text; duplicate surfaces 409
    - [ ] `n/a` cells (e.g. a view with POST/PUT/DELETE) are disabled
    - [ ] Wrong/absent key on a now-gated endpoint → 401/403 message surfaced
    - [ ] Existing `api-keys` page regression → key CRUD + delete still works
    - [ ] api-generated page regression → endpoints + Copy cURL intact, no "API Key Access" card

   **Proven without a browser** (the parts of the list that are really about data, not pixels —
   covered by the Step 12 runtime table and Step 10's matrices): `n/a` cells derive from the now
   server-supplied `verb`; wrong/absent key on a gated endpoint returns 401/403 whose exact codes and
   messages the component surfaces verbatim; deny precedence and the "grant over deny" rejection hold;
   both mandatory warnings are computed from `schemaObjects` + `orgPermissions` at click time.
   **Still genuinely unverified** are the purely visual/interaction items: tab switching, the two
   confirm dialogs actually appearing with the right copy, the `■`→`░` transition being *seen*, and
   the `api-keys` / `api-generated` page regressions.

---

## 📌 Operational note — getting a token for scripted API checks

Worth recording, because it cost real time and will recur. `POST /api/auth/login` is an OAuth2
**code exchange**, so it cannot be scripted; going straight to the Keycloak token endpoint does not
work either, and the reason is not obvious from the error:

- The **live** `api-engine-app` client (as opposed to the checked-in `realm-config.json`, which does
  not carry it) is `publicClient: false`, has `directAccessGrantsEnabled: true`, and has
  `clientAuthenticatorType: "client-jwt"` — i.e. it authenticates with a **private-key JWT**.
- A password grant with the correct `client_secret` still fails with
  `invalid_client: "Parameter client_assertion_type is missing"`, because the client will not accept
  secret auth at all.
- The fix used here: build the same assertion the backend already builds in
  `KeycloakService.AddClientAssertion` / `GenerateClientAssertion` — sign an RS256 JWT over
  `{iss, sub, aud=<token endpoint>, jti, iat, exp}` with the private key in
  `appsettings.json → KeyClock:ClientJwtKey`, then send it as `client_assertion_type` +
  `client_assertion`. That returns a normal user token carrying the `organization` claim.
- The realm's only user is `fadi.freij@hotmail.com` and its password is not recoverable from the repo,
  so verification used a **temporary user** in the *Test Organization* group, deleted afterwards
  (the realm is back to its single original user).
- The metadata endpoint's query parameter is `table`, not `objectName`:
  `GET /api/schema/{workspaceId}/columns?table=<name>`.

---

## Progress Tracking

| Step | Description | Status |
|---|---|---|
| 0 | Prerequisites verified (ApiKey table/controller/repo/migration, FE pages, baseline builds) | ✅ DONE |
| 1 | `ApiKeyPermission` entity (nullable `ObjectName`/`Verb` + `IsDeny`) | ✅ DONE |
| 2 | EF `DbSet` + relationship + **cascade delete** + unique index (`IsDeny` excluded) | ✅ DONE |
| 3 | Migration `AddApiKeyPermission` + apply + prove `NULL`-row uniqueness | ✅ DONE (NULL-uniqueness proof **overridden** — divergence **B**) |
| 4 | Repository interface + impl (`GetInScopeAsync`, `ExistsDenyAsync`, `ExistsBroaderScopeAsync`) | ✅ DONE |
| 5 | DTOs (nullable `ObjectName`/`Verb` + `IsDeny`) | ✅ DONE |
| 6 | `ApiKeyHasher` shared static + refactor `ApiKeyController` | ✅ DONE |
| 7 | `ApiKeyPermissionController` (JWT-only CRUD + additive-only validation) | ✅ DONE |
| 8 | DI registration (repo + filter) | ✅ DONE |
| 9 | `ApiKeyAuthFilter` + `[ServiceFilter]` on CRUD + `[Authorize]` on metadata | ✅ DONE |
| 10 | Backend verification (build + manual matrix) | ✅ DONE (filter **38/38** + controller **11/11**; Bug 1 & Bug 2 both fixed) |
| 11 | FE Scopes service `api-key-scopes.service.ts` (DTOs + permission CRUD; reuse `exploreSchema`) | ✅ DONE |
| 12 | FE Scopes tab component (workspace + key pickers, `■ ⊘ ░ □ n/a` grid state) | ✅ DONE (+ additive BE change: `SchemaObjectDto.Verb`) |
| 13 | FE template: Scopes tab grid + mandatory warnings; remove api-generated "API Key Access" card | ✅ DONE (card removal = **no-op**, none existed; D5 cURL **implemented new**) |
| 14 | Frontend verification | 🚧 IN PROGRESS — build ✅; data layer ✅ end-to-end; browser click-through outstanding |

---

## Key decisions fixed by the plan (do not deviate)

| # | Decision |
|---|---|
| D1 | **Default-allow** — no permission row ⇒ anonymous requests pass through. |
| D2 | **Wildcards supported.** `ObjectName` / `Verb` are **nullable**; `NULL` = any. Four cases: `(NULL, NULL)`, `(NULL, verb)`, `(object, NULL)`, `(object, verb)`. The wildcard is `NULL`, **not** `"*"` — the API accepts `null`, rejects `"*"`, and an **empty string is not a wildcard** (400). |
| D3 | Permissions CRUD lives in a **new `ApiKeyPermissionController`**; `ApiKeyController` untouched except hasher extraction. |
| D5 | FE gets **Copy cURL with `X-Api-Key`** on the api-generated page — unchanged, `YOUR_API_KEY_HERE` placeholder. |
| D8 | Permission add/remove lives in a **"Scopes" tab on the API Keys page**, as a **per-key drill-down** (workspace picker + key picker). The api-generated page keeps its endpoints and Copy cURL and **loses** the "API Key Access" card. |
| D9 | Metadata endpoint stays **JWT-only**. |
| D10 | **Org containment on grants, enforced twice.** Controller (`plan §5.3`): route `apiKeyId` + body `workspaceId` + JWT `organization` claim must all resolve to the **same** organization → else 403. Filter (`plan §4.2` step 5b): `apiKey.OrganizationId == workspace.OrganizationId`, else 403 `API_KEY_WRONG_ORGANIZATION` — defence-in-depth so a pre-existing or hand-inserted bad row can never escalate. |
| D11 | **API-key-only — JWT on the dynamic CRUD endpoints is deferred** to a later story. Keep `//[Authorize]` at `DynamicApiController.cs:19` **commented out** and add `[Authorize]` to `GetObjectColumns` only. Consequence: the filter **must not read the `organization` claim** — the caller's org comes solely from `apiKey.OrganizationId` (step 5b). |
| D12 | **`IsDeny` — object-scoped deny only.** `IsDeny = 1` is permitted **only when `ObjectName` is non-null**: **L1** `(key, object, verb, ws, db, deny)`, **L2** `(key, object, NULL, ws, db, deny)`. `IsDeny = 1` with `ObjectName == null` → **400 at every level**. Deny **always wins** (no lattice — two existence checks suffice because every deny is maximally specific). `IsDeny` is **excluded from the unique index**, so grant+deny at the same 5-tuple cannot coexist. Only **additive** changes through the CRUD API; **409** = exact duplicate, **400** = redundant scope / deny conflict / invalid field. |
| D13 | **The D1 fallback matches the exact tuple.** A partial grant protects **only the verbs it names**: granting `GET` on `Orders` leaves `POST /Orders` and `DELETE /Orders` returning **200 to anyone, no key required**. Accepted consequence, load-bearing — do not "fix" the `200` in the Step 10 / plan §9 matrix, and do not widen the predicate. |

## Risks (from plan §10) to keep in mind while implementing

- **Hash drift:** the filter and `ApiKeyController` must share `ApiKeyHasher` (Step 6) — otherwise keys silently never match.
- **Default-allow is intentional** — do not invert to deny-by-default in this story.
- **Exact-tuple fallback (D13) leaves other verbs world-writable.** A `GET`-only grant on `Orders` means `POST`/`DELETE /Orders` are **200 anonymous**. That `200` in the verification matrix is the designed behaviour, not a defect.
- **A deny row is itself a gating row.** Adding one makes the predicate match, so the endpoint becomes key-required for *everyone*; on an ungranted object that is a 200-for-everyone → 401-for-everyone flip. The FE must warn, and any surprising 401 should be read as "a deny row exists here".
- **`IsDeny` must stay out of the unique index** — it is the mechanism preventing grant+deny at the same scope, and removing it would create a cell no `DELETE` can resolve. Keep the explanatory comment in place.
- **Case sensitivity is a collation question now**: `ObjectName` is matched inside `GetInScopeAsync` (SQL), not in C#. If the dev DB is case-sensitive, `LOWER()` both sides or a CI collation is required. → **Resolved for the dev DB:** `utf8mb4_0900_ai_ci` is already case-insensitive, so no `LOWER()`.
- **`LastUsedAt` write is best-effort** — never fail the request on a usage-record failure.
- **DB round-trips:** filter adds 1–4 queries per gated request (in-scope rows, workspace, key, up to 2 deny probes); caching is documented follow-up, not in scope.
- **`DatabaseName` is denormalised at grant time**, so a workspace DB rename silently re-opens everything: stored rows keep the old name, the filter queries the new one, `rows` is empty, D1 allows. A DB-rename path must migrate the `ApiKeyPermission` rows in the same transaction, or be blocked.
- **Workspace-narrowing trap:** removing a broad grant re-opens **every other object and every other workspace** the key reached through that row — a key can hold `(NULL, NULL, W1, SalesDb)` and `(NULL, NULL, W2, SalesDb2)`, and deleting the first silently re-opens all of `W2`. The confirmation dialog must say so, not just list the objects in the current grid. Conversely, deny rows **survive** narrowing: a deny does not depend on the broader grant existing, so deleting a broad grant leaves a denied object at 403.
- **Redundancy is semantic, not structural** — the unique index cannot see `(k, NULL, GET, …)` vs `(k, Orders, GET, …)`, so `ExistsBroaderScopeAsync` is the only guard. Skipping it degrades the UI (a `□` that 400s on click) but opens no security hole: deny still wins and grant+deny at the same tuple remain impossible.
- **Cascade delete is required**, not cosmetic: EF's default `ClientSetNull` throws when dependents exist, so `DELETE /api/keys/{id}` would 500 on any key that has ever been granted a scope.
- **Org containment is load-bearing (D10):** `ApiKeyPermission` has no `OrganizationId` of its own — org isolation is transitive via `ApiKey.OrganizationId`. Filter step 5b is not cosmetic; remove it and a single bad row silently grants one org read access to another org's database. Also do **not** "optimize" `GetInScopeAsync` by dropping `workspaceId` from the predicate — org separation currently rests on `workspaceId` being a globally unique Guid.
- **No claim in the filter (D11) — a conditional claim check is a bypass.** There is no JWT on the CRUD actions, so `User.FindFirst("organization")` is `null` for the primary caller (an external client sending only `X-Api-Key`). The filter can only do `if (claim != null) checkOrg()` — which an attacker defeats by simply omitting the `Authorization` header — or `if (claim == null) → 403` — which breaks every API-key client, i.e. the whole feature. Neither is safe. Org is proven by the resolved key only: `apiKey.OrganizationId` vs `workspace.OrganizationId`, both `Guid`s already in hand, **zero extra queries**. Do not add a token→org-name→`Organizations.Id` lookup to the filter.
- **Pointer to prior art:** the plan references `ApiKeyPermission` as the future home of per-object auth-mechanism choice (Keycloak etc.) — keep the schema extensible. In practice: keep `IsDeny` a flag on this table rather than splitting a `Deny` table, keep the `NULL` wildcard convention intact so a future `"*"` scheme never needs a translate layer, and do not hard-code assumptions beyond the fields specified.
- **Known pre-existing IDOR, out of scope:** `ApiKeyController.GetByOrganizationId` (`ApiKeyController.cs` lines 41–52) never compares the route `{organizationId}` to the caller's JWT `organization` claim, so any authenticated user can list any org's keys. The Scopes tab's key picker inherits that exposure — it wants its own ticket. → Step 7's new `GET /api/permissions` does **not** repeat this: it rejects a foreign `?organizationId=` with 403. The pre-existing endpoint is unchanged.
