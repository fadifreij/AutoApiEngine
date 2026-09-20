# Implementation — DynamicApiService Refactor + SP/Function HTTP Verb Handling

> Companion to `doc/ApiDynamic/plan.md` (design) and `requirement.md` (source of truth).
> **How to use:** each step has a **Status** marker. When a step is finished and verified, change its marker to `✅ DONE`. Work is committed directly to `master` (no branches). `dotnet build` must stay green after every step.

---

## Status Legend

| Marker | Meaning |
|---|---|
| ⬜ PENDING | Not started |
| 🚧 IN PROGRESS | Currently being worked on |
| ✅ DONE | Finished + builds pass |

---

## Step 1 — DTOs: routine params + execution response + metadata extension

**Status:** ✅ DONE

**Goal:** Add the DTOs the rest of the feature depends on.

**Files:**
- `BE/AutoApiEngine.ServiceAbstraction/DTO/RoutineParameterDto.cs` (new)
- `BE/AutoApiEngine.ServiceAbstraction/DTO/DynamicApiExecutionResponse.cs` (new)
- `BE/AutoApiEngine.ServiceAbstraction/DTO/DynamicApiObjectMetadataDto.cs` (extend)

**Tasks:**
1. Create `RoutineParameterDto`:
   ```csharp
   public class RoutineParameterDto
   {
       public string Name { get; set; } = string.Empty;
       public string DataType { get; set; } = string.Empty;
       public string ParameterMode { get; set; } = "IN";   // "IN" | "OUT" | "INOUT"
       public bool HasDefault { get; set; }
       public string? DefaultValue { get; set; }
       public int OrdinalPosition { get; set; }
   }
   ```
2. Create `DynamicApiExecutionResponse` (per plan §3.5):
   ```csharp
   public class DynamicApiExecutionResponse
   {
       public List<Dictionary<string, object?>>? ResultSets { get; set; }
       public Dictionary<string, object?>? OutputParams { get; set; }
       public int RowsAffected { get; set; }
   }
   ```
3. Extend `DynamicApiObjectMetadataDto`:
   ```csharp
   public string? Verb { get; set; }                                // "GET" | "POST" for SP, "GET" for View/Function, null for Table
   public List<RoutineParameterDto> Parameters { get; set; } = new();
   ```

**Verify:** `dotnet build BE/AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj`

---

## Step 2 — `IDynamicApiService` new method + `SpVerb` enum + `SpVerbClassifier`

**Status:** ✅ DONE

**Goal:** Add the routine-execution contract and the 3-rule classifier (plan §3.3).

**Files:**
- `BE/AutoApiEngine.ServiceAbstraction/IDynamicApiService.cs` (extend)
- `BE/AutoApiEngine.Domain/Enums/SpVerb.cs` (new)
- `BE/AutoApiEngine.Services/DatabaseManagementServices/DynamicApiService/SpVerbClassifier.cs` (new)

**Tasks:**
1. Add to `IDynamicApiService`:
   ```csharp
   Task<DynamicApiExecutionResponse> ExecuteRoutineAsync(
       string workspaceId,
       string objectName,
       Dictionary<string, object?> parameters,
       CancellationToken cancellationToken = default);
   ```
2. Add enum:
   ```csharp
   public enum SpVerb { Get, Post }
   ```
3. Create `SpVerbClassifier` implementing the 3 rules literally:
   - `SpVerb Evaluate(string definition)`
   - Normalize: strip `--`/`/* */`/`#` comments and the `CREATE PROCEDURE ... AS` header, lowercase, trim trailing `;`/whitespace.
   - Unwrap final statement; for SQL Server skip trailing wrapper `END` (bodies are often `BEGIN ... END`).
   - Match:
     - last statement starts with `select` → **Get**
     - last statement starts with `exec`/`execute` and its text contains `select` → **Get**
     - anything else → **Post**

**Verify:** `dotnet build` passes.

---

## Step 3 — `DynamicApiServiceBase` (abstract, all shared code from the old class)

**Status:** ✅ DONE

**Goal:** Move all dialect-agnostic orchestration out of the old `DynamicApiService.cs`.

**File:** `BE/AutoApiEngine.Services/DatabaseManagementServices/DynamicApiService/DynamicApiServiceBase.cs` (new)

**Tasks:**
1. `public abstract class DynamicApiServiceBase : IDynamicApiService` — namespace stays `AutoApiEngine.Services.DatabaseManagementServices`.
2. Copy in (dialect-agnostic) from old `DynamicApiService.cs`:
   - CRUD orchestration: `GetListAsync`, `GetByIdAsync`, `GetByCompositeKeyAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, plus `ExecuteRoutineAsync` (delegates to abstract hook)
   - `LoadWorkspaceAsync`, `LoadWorkspaceAndMetaAsync`, `GetTableMetadataAsync` shape, `GetObjectColumnsAsync` shape
   - `ExtractRelationNames`, join building, `BuildSelectList`, `BuildWhereClause`, `BuildOrderByClause`, `BuildCountQuery`
   - `CreateParam`, `ConvertToNativeValue`, `EscapeLike`
   - `ExecuteQueryAsync`, `ExecuteNonQueryAsync`, `ExecuteScalarAsync<T>`
   - Constants `MaxInValues`, `MaxPageSize`, `DefaultPageSize`
3. Add dialect hooks as `protected abstract` members (plan §2.2): `QuoteIdentifier`/`QuoteClose`, `GetProviderFactory`, `GetColumnsSql`, `GetObjectColumnsSql`, `ResolveObjectSqlAndType`, `ApplyPaging`, `InsertReturnRowAsync`, `UpdateReturnRowAsync`, `DeleteReturnRowAsync`, `BuildConnectionString`, `ExecuteRoutineInternalAsync`.
4. Replace every `engine == DatabaseEngine.MySql ? ... : ...` inline branch with a call to the corresponding hook.
5. `internal record ColumnInfo` / `TableMetadata` and `ResolveObjectStatic` stay available for `DynamicApiMetadataService` (keep `internal`).

**Verify:** `dotnet build` passes (old class still exists, so nothing breaks yet).

---

## Step 4 — `SqlServerDynamicApiService` + `MySqlDynamicApiService`

**Status:** ✅ DONE

**Goal:** Two concrete dialect implementations (plan §2.2). SQL Server (`EXEC`, `OUTPUT`, OFFSET/FETCH) and MySQL (`CALL`, `LAST_INSERT_ID()`, LIMIT).

**Files:**
- `BE/AutoApiEngine.Services/DatabaseManagementServices/DynamicApiService/SqlServerDynamicApiService.cs` (new)
- `BE/AutoApiEngine.Services/DatabaseManagementServices/DynamicApiService/MySqlDynamicApiService.cs` (new)

**Tasks (both classes):**
1. Implement every abstract hook from base.
2. SQL Server: `` `[` `]` `` quoting, `SqlClientFactory`, `GetSqlColumnsSql()`, `OFFSET ? ROWS FETCH NEXT ? ROWS ONLY`, `OUTPUT INSERTED.*` / `OUTPUT DELETED.*`, `sqlConnectionStringBuilder`.
3. MySQL: `` ` `` quoting, `MySqlClientFactory`, `GetMySqlColumnsSql()`, `LIMIT ? OFFSET ?`, follow-up `SELECT ... LAST_INSERT_ID()` / `SELECT` after UPDATE, `ExecuteNonQuery` for DELETE, `MySqlConnectionStringBuilder`.
4. `ExecuteRoutineInternalAsync`:
   - SQL Server: `EXEC [schema].[proc] @p1 = @in1, @p2 = @in2 OUTPUT ...`; scalar fn → `SELECT dbo.fn(@p1)`; table-valued fn → `SELECT * FROM dbo.fn(@p1, ...)`. Use `DbParameter.Direction.Output`, iterate `reader.NextResultAsync`.
   - MySQL: `CALL proc(@p1, @p2, ...)` with `Direction.Output`/`InputOutput`; scalar fn → `SELECT fn(@p)`. Use `reader.RecordsAffected`.

**Verify:** `dotnet build` passes.

---

## Step 5 — `DynamicApiServiceResolver`

**Status:** ✅ DONE

**Goal:** Dispatching facade by `DatabaseEngine` (plan §2.2). Matches existing resolver patterns.

**File:** `BE/AutoApiEngine.Services/DatabaseManagementServices/DynamicApiService/DynamicApiServiceResolver.cs` (new)

**Tasks:**
1. `public class DynamicApiServiceResolver : IDynamicApiService`
2. Constructor: `(IWorkspaceRepository repo, SqlServerDynamicApiService sql, MySqlDynamicApiService mysql)`.
3. Each public method: load workspace once → `workspace.DatabaseEngine switch { MySql => _mysql..., _ => _sql... }` → delegate using the workspace-accepting overload (avoids double workspace load).
4. Expose `internal` metadata helpers the `DynamicApiMetadataService` needs: `GetTableMetadataAsync(Workspace, ...)`, `BuildConnectionString`, `CreateConnection`, `ResolveObjectStatic`, plus routine-definition + parameter helpers (see Step 7).

**Verify:** `dotnet build` passes.

---

## Step 6 — Delete old `DynamicApiService.cs`, rewire DI + metadata service

**Status:** ✅ DONE

**Goal:** Remove the monolith; register the new classes (plan §2.4).

**Files:**
- `BE/AutoApiEngine.Services/DatabaseManagementServices/DynamicApiService.cs` (delete)
- `BE/AutoApiEngine.Services/DatabaseManagementServices/DynamicApiMetadataService.cs` (update)
- `BE/AutoApiEngine.ApiServices/Providers/ServiceCollectionExtensions.cs` (update)

**Tasks:**
1. Delete old `DynamicApiService.cs`.
2. `ServiceCollectionExtensions`: replace old registrations with
   ```csharp
   services.AddScoped<SqlServerDynamicApiService>();
   services.AddScoped<MySqlDynamicApiService>();
   services.AddScoped<DynamicApiServiceResolver>();
   services.AddScoped<IDynamicApiService>(sp => sp.GetRequiredService<DynamicApiServiceResolver>());
   services.AddScoped<SpVerbClassifier>();
   ```
3. `DynamicApiMetadataService`: change dependency from concrete `DynamicApiService` → `DynamicApiServiceResolver`; update all member calls (`GetTableMetadataAsync`, `BuildConnectionString`, `CreateConnection`, `ResolveObjectStatic`).

**Verify:** `dotnet build` passes (this is the first big wiring step — errors here are expected and must be fixed before continuing).

---

## Step 7 — Routine definitions + parameters in metadata; wire `Verb` + `Parameters`

**Status:** ✅ DONE

**Goal:** Metadata endpoint returns `Verb` and `Parameters` for SPs/Views/Functions (plan §3.4).

**Files:**
- `BE/AutoApiEngine.Services/DatabaseManagementServices/DynamicApiService/` (resolver + concrete services; add routine-definition/param resolution)
- `BE/AutoApiEngine.Services/DatabaseManagementServices/SqlSchemaExplorer.cs` (extend)
- `BE/AutoApiEngine.Services/DatabaseManagementServices/MySqlSchemaExplorer.cs` (extend)
- `BE/AutoApiEngine.ServiceAbstraction/ISchemaExplorerService.cs` (extend)
- `BE/AutoApiEngine.Services/DatabaseManagementServices/DynamicApiMetadataService.cs` (fill `Verb` + `Parameters`)

**Tasks:**
1. Add schema-explorer methods (or resolver helpers) to fetch routine parameters:
   - SQL Server: `sys.parameters` + `sys.types` → `Name, Type, is_output, has_default_value, default_value, parameter_id`.
   - MySQL: `information_schema.PARAMETERS` → `PARAMETER_NAME, DTD_IDENTIFIER, PARAMETER_MODE, ORDINAL_POSITION`.
2. Metadata fill logic in `GetObjectMetadataAsync`:
   - Table → `Verb = null`, `Parameters = []`
   - View → `Verb = "GET"`, `Parameters` resolved like Functions (from view definition)
   - Function → `Verb = "GET"`, resolve params
   - StoredProcedure → resolve params + fetch definition → `SpVerbClassifier.Evaluate(definition)` → map to `Verb`
3. Ensure the definition source matches the one the classifier's normalization handles (leading `CREATE PROCEDURE` header, SQL Server `BEGIN/END`).

**Verify:** `dotnet build` passes. Manual: `GET /api/schema/{ws}/columns?table=<sp>` returns `verb` + `parameters`.

---

## Step 8 — `DynamicApiController` branching + 405 guard + param binding

**Status:** ✅ DONE

**Goal:** Route GET/POST by object type to the right backend path (plan §3.6).

**File:** `BE/AutoApiEngine.Presentation/Controllers/DynamicApiController.cs`

**Tasks:**
1. In `GetList` (GET) and `Create` (POST), resolve object type via `_metadataService` (classify once, cache in request scope).
2. Branch:
   - Table → current behavior (`GetListAsync` / `CreateAsync`)
   - View → `GetListAsync` with query-string params bound as `WHERE` conditions
   - SP classified **GET** & verb GET → bind query params → `ExecuteRoutineAsync`
   - SP classified **POST** & verb POST → bind body → `ExecuteRoutineAsync`
   - Function → `ExecuteRoutineAsync` (params from query string)
3. 405 guard: incoming verb must match the classified verb; otherwise `StatusCode(405, ...)` with clear message.
4. Param binding: GET → `Request.Query` (match by name) → `Dictionary<string, object?>`; POST → `[FromBody] Dictionary<string, object?>`. Missing required params → `400 Bad Request` listing missing names.

**Verify:** `dotnet build` passes. Manual: SP GET/POST + wrong verb → 405.

---

## Step 9 — Frontend: service DTOs + execution calls

**Status:** ✅ DONE

**Goal:** FE can fetch metadata with verb/params and execute routines (plan §3.7).

**File:** `FE/src/app/dashboard/api-generated/dynamic-api.service.ts`

**Tasks:**
1. Extend `ObjectMetadata`: add `verb?: 'GET' | 'POST'` and `parameters: RoutineParameter[]`.
2. Add `RoutineParameter` interface:
   ```ts
   export interface RoutineParameter {
     name: string;
     dataType: string;
     parameterMode: 'IN' | 'OUT' | 'INOUT';
     hasDefault: boolean;
     defaultValue?: string;
     ordinalPosition: number;
   }
   ```
3. Add `DynamicApiExecutionResponse` interface (`resultSets`, `outputParams`, `rowsAffected`).
4. Add `executeRoutineGet(workspaceId, objectName, params: Record<string, any>)` → `http.get` and `executeRoutinePost(workspaceId, objectName, body: Record<string, any>)` → `http.post`, both `withCredentials: true`.

**Verify:** `npm run build` from `FE/`.

---

## Step 10 — Frontend: `generatedUrls` branching + parameter/Run UI

**Status:** ✅ DONE

**Goal:** Correct endpoint display + run UI per object type (plan §3.7).

**Files:**
- `FE/src/app/dashboard/api-generated/api-generated.ts`
- `FE/src/app/dashboard/api-generated/api-generated.html`

**Tasks:**
1. `generatedUrls` computed branches on `objectType`:
   - Table → current 5 URLs
   - View → 1 GET URL (list), query string from `parameters`; **no** `/{id}`
   - Function → 1 GET URL (run), query string from `parameters`
   - SP → 1 URL only: `GET` if `verb === 'GET'`, `POST` if `verb === 'POST'`; no `/{id}`
2. Build GET query string from `parameters` (`?param=value` placeholders); build POST body template from IN params using existing `generateExampleValue`.
3. HTML: for SP/Function/parametrized View show parameter inputs + **Run** button + response viewer (sections for `resultSets`, `outputParams`, `rowsAffected`); keep copy-button UX for tables/views without params.
4. Wire `executeRoutineGet` / `executeRoutinePost` to the Run button.

**Verify:** `npm run build` from `FE/`.

---

## Step 11 — Full verification

**Status:** ✅ DONE

**Goal:** End-to-end checks per plan §5 on both engines.

**Tasks:**
1. Backend: `dotnet build BE/AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj`
2. Frontend: `npm run build` from `FE/`
3. Manual checks (SQL Server + MySQL):
   - Table → 5 endpoints, CRUD works (regression)
   - View → 1 GET; parametrized view → `?param=value` filters (incl. PK lookup)
   - GET-classified SP (ends with SELECT) → 1 GET URL; `?param=value` → `resultSets`
   - POST-classified SP (INSERT/UPDATE/DELETE/mixed) → 1 POST URL; `outputParams`/`rowsAffected` populated for OUT params
   - SP with no params → Run with `{}` / empty query
   - Function → 1 GET URL, returns scalar/table
   - Wrong verb on SP → **405**
4. Update this doc: mark Step 11 ✅ DONE when all pass.

---

## Progress Tracking

| Step | Description | Status |
|---|---|---|
| 1 | DTOs (params + execution response + metadata extension) | ✅ DONE |
| 2 | `IDynamicApiService.ExecuteRoutineAsync` + `SpVerb` + `SpVerbClassifier` | ✅ DONE |
| 3 | `DynamicApiServiceBase` (shared code from old class) | ✅ DONE |
| 4 | `SqlServerDynamicApiService` + `MySqlDynamicApiService` | ✅ DONE |
| 5 | `DynamicApiServiceResolver` | ✅ DONE |
| 6 | Delete old class, rewire DI + metadata service | ✅ DONE |
| 7 | Routine params/verb in metadata (`Verb` + `Parameters`) | ✅ DONE |
| 8 | Controller branching + 405 guard + param binding | ✅ DONE |
| 9 | Frontend service DTOs + execution calls | ✅ DONE |
| 10 | Frontend `generatedUrls` branching + Run UI | ✅ DONE |
| 11 | Full verification (builds + manual on both engines) | ✅ DONE |