# Implementation Plan — DynamicApiService Refactor + SP/Function HTTP Verb Handling

> Source of truth: `doc/ApiDynamic/requirement.md` (updated) + `doc/ApiDynamic/sp_verb_problem.md`
> Scope decision (confirmed with user): verb classification in **backend** (exposed via metadata) · SP/function execution returns **outputParams + resultSets + rowsAffected** now · **all four object types** (Table, View, StoredProcedure, Function) handled now · only **SQL Server + MySQL** implemented now (SQLite/PostgreSQL later).

---

## 1. What problem are we solving

### 1a. DynamicApiService is one monolith mixing two SQL dialects
`BE/AutoApiEngine.Services/DatabaseManagementServices/DynamicApiService.cs` (1081 lines, `IDynamicApiService`) branches on `isMySql` / `DatabaseEngine.MySql` in ~12 places:

| Concern | MySQL | SQL Server |
|---|---|---|
| Identifier quoting | `` ` `` | `[ ]` |
| Connection string (`BuildConnectionString`) | `MySqlConnectionStringBuilder` style | `SqlConnectionStringBuilder` |
| Provider factory | `MySqlClientFactory` | `SqlClientFactory` |
| Column metadata SQL | `GetMySqlColumnsSql()` | `GetSqlColumnsSql()` |
| Object resolve SQL | `information_schema.tables ∪ routines` | 3 separate INFORMATION_SCHEMA queries |
| Paging | `LIMIT ? OFFSET ?` | `OFFSET ? ROWS FETCH NEXT ?` |
| INSERT return-row | follow-up `SELECT ... LAST_INSERT_ID()` | `OUTPUT INSERTED.*` |
| UPDATE return-row | follow-up `SELECT` | `OUTPUT INSERTED.*` |
| DELETE return-row | `ExecuteNonQuery` + affected count | `OUTPUT DELETED.*` |

Adding SQLite/PostgreSQL later currently means editing this one class repeatedly → **violates Open/Closed** (the user's exact complaint). Fix: per-database implementations + shared base = the same pattern already used by `SchemaExplorerResolver`, `DatabaseManagementServiceResolver`, `ForeignKeyServiceResolver`.

### 1b. Stored procedures don't work at all today
`DynamicApiService` executes `SELECT ... FROM [schema].[object]` for every object. On an SP that fails. The frontend `generatedUrls` also renders the same 5 CRUD URLs for every object type → wrong for SPs.

---

## 2. PART A — Refactor: per-database DynamicApiService

### 2.1 Target folder layout

```
BE/AutoApiEngine.Services/DatabaseManagementServices/DynamicApiService/
├── DynamicApiServiceBase.cs            // new — abstract, ALL shared orchestration + dialect hooks
├── SqlServerDynamicApiService.cs       // new — SQL Server dialect bits
├── MySqlDynamicApiService.cs           // new — MySQL dialect bits
├── DynamicApiServiceResolver.cs        // new — dispatching facade, implements IDynamicApiService
└── SpVerbClassifier.cs                 // new — the 3-rule classifier (Part B)
```

The old `DatabaseManagementServices/DynamicApiService.cs` is **deleted** (its logic moves into the above). Namespace can stay `AutoApiEngine.Services.DatabaseManagementServices` to avoid touching every `using`.

### 2.2 Class responsibilities

**`DynamicApiServiceBase : IDynamicApiService` (abstract)**
Keeps everything that is dialect-agnostic and that calls the dialect hooks:
- Public CRUD orchestration: `GetListAsync`, `GetByIdAsync`, `GetByCompositeKeyAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, new `ExecuteRoutineAsync` (Part B)
- Workspace loading, `LoadWorkspaceAndMetaAsync`, `GetTableMetadataAsync` shape, FK/relation expansion, `ExtractRelationNames`, join building, `BuildSelectList`, `BuildWhereClause` (operators eq/neq/gt/...), `BuildOrderByClause`, `BuildCountQuery`
- Parameter factories: `CreateParam`, `ConvertToNativeValue`, `EscapeLike`
- `ExecuteQueryAsync`, `ExecuteNonQueryAsync`, `ExecuteScalarAsync<T>`
- Constants `MaxInValues`, `MaxPageSize`, `DefaultPageSize`

Dialect hooks (abstract / virtual members):
- `protected abstract string QuoteIdentifier()` and `protected abstract string QuoteClose`
- `protected abstract DbProviderFactory GetProviderFactory()`
- `protected abstract string GetColumnsSql()`
- `protected abstract string GetObjectColumnsSql()`
- `protected abstract string ResolveObjectSqlAndType(...)`
- `protected abstract string ApplyPaging(string selectSql, int pageSize, int offset)`
- `protected abstract Task<Dictionary<string,object?>?> InsertReturnRowAsync(...)` (OUTPUT vs LAST_INSERT_ID follow-up)
- `protected abstract Task<Dictionary<string,object?>?> UpdateReturnRowAsync(...)`
- `protected abstract Task<Dictionary<string,object?>?> DeleteReturnRowAsync(...)`
- `protected abstract string BuildConnectionString(Workspace workspace)`
- `protected abstract Task<DynamicApiExecutionResponse> ExecuteRoutineInternalAsync(...)` (Part B: EXEC vs CALL)

**`SqlServerDynamicApiService : DynamicApiServiceBase`**
Implements `[ ]`, `SqlClientFactory`, `GetSqlColumnsSql()`, OFFSET/FETCH, `OUTPUT INSERTED.*` / `OUTPUT DELETED.*`, `SqlConnectionStringBuilder`, and `EXEC [schema].[proc] @p = @param ...` execution.

**`MySqlDynamicApiService : DynamicApiServiceBase`**
Implements `` ` ``, `MySqlClientFactory`, `GetMySqlColumnsSql()`, LIMIT/OFFSET, follow-up `SELECT ... LAST_INSERT_ID()`, `ExecuteNonQuery`, `MySqlConnectionStringBuilder`, and `CALL proc(@p1,@p2 ...)` execution.

**`DynamicApiServiceResolver : IDynamicApiService`**
Constructor: `(IWorkspaceRepository repo, SqlServerDynamicApiService sql, MySqlDynamicApiService mysql)`.
Each public method: load workspace once → `engine switch { MySql → _mysql, _ → _sql }` then delegate. Also exposes the internal metadata helpers the `DynamicApiMetadataService` needs (`GetTableMetadataAsync`, `BuildConnectionString`, `CreateConnection`, `ResolveObjectStatic`, new routine-verb/param helpers) — same assembly, `internal` OK.

> Design note — avoiding double workspace load for SP execution: the resolver resolves the workspace, then calls the engine service's workspace-accepting overload (`...Async(Workspace, ...)`), so the workspace is loaded **once** per request. The `workspaceId`-based interface entry points remain for direct `IDynamicApiService` callers.

### 2.3 `IDynamicApiService` interface changes

Add (Part B):
- `Task<DynamicApiExecutionResponse> ExecuteRoutineAsync(string workspaceId, string objectName, Dictionary<string,object?> parameters, CancellationToken ct)` — single method used by both SP GET and SP POST execution (the controller controls verb; the service always `EXEC`/`CALL`).

Keep existing 6 methods unchanged (backward compatible for `DynamicApiController`).

### 2.4 DI registration change (`BE/AutoApiEngine.ApiServices/Providers/ServiceCollectionExtensions.cs`)

```csharp
services.AddScoped<SqlServerDynamicApiService>();
services.AddScoped<MySqlDynamicApiService>();
services.AddScoped<DynamicApiServiceResolver>();
services.AddScoped<IDynamicApiService>(sp => sp.GetRequiredService<DynamicApiServiceResolver>());
services.AddScoped<SpVerbClassifier>();
```
(`DynamicApiMetadataService` switches from `DynamicApiService` → `DynamicApiServiceResolver`.)

### 2.5 Tradeoffs considered

- **Option A — per-db classes + shared base + resolver (RECOMMENDED).** Matches existing codebase pattern exactly; SQLite/PostgreSQL later = 1 new class + `DatabaseEngine` arm in resolver. No interface break.
- **Option B — one class + injected "SQL dialect strategy" per engine.** Fewer files, but diverges from the resolver convention used everywhere else; strategy still needs the same hooks.
- **Option C — leave as-is + `if` per new engine.** Rejected — this is the exact problem the user reported.

---

## 3. PART B — Stored Procedure & Function HTTP verbs

### 3.1 The classification rules (from updated requirement.md)

Per **`requirement.md` lines 10–16**:

1. If the **end** of the content contains `select` → **GET**
2. If the **end** is `execute` and **inside the execute** there is `select` → **GET**
3. **Otherwise → POST**

⇒ an SP is exposed with **exactly one verb** (GET or POST), never `/{id}`, never PUT/DELETE.

Views and Functions: **always GET** (read/run) — no content analysis. Views and Functions expose their **input parameters in the query string** (same handling). Views have **no `/{id}` route** — filtering by PK is just `?param=value` on the list endpoint (a special case of sending parameters by URL).
Tables: unchanged, all 5 CRUD verbs.

### 3.2 Exposed endpoints per object type (final mapping)

| Object type | Exposed actions | Notes |
|---|---|---|
| Table | GET list · GET by id · POST · PUT · DELETE | unchanged |
| View | GET list | write verbs hidden (read-only data source) · no `/{id}` — PK lookup is just `?param=value` on the list endpoint · input params → query string (same as Function) |
| Function | GET (run) | input params → query string |
| StoredProcedure | **GET** *or* **POST** (single) | GET input → query string · POST input → JSON body |

### 3.3 `SpVerbClassifier` (backend, per Q2)

New class `SpVerbClassifier` with `SpVerb Evaluate(string definition)` implementing the 3 rules literally:

1. Normalize: strip leading `--`/`/* */`/`#` comments and `CREATE PROCEDURE ... AS` header (if using full definition), lowercase, trim trailing `;` and spaces.
2. Unwrap final statement: split on statement boundaries; for SQL Server also skip a trailing wrapper `END` (T-SQL bodies are often `BEGIN ... END`); the *last real statement* is the one inspected.
3. Rule match:
   - last statement starts with `select` → **GET**
   - last statement starts with `exec`/`execute` and its text contains `select` → **GET**
   - anything else → **POST**
4. Inputs: the routine **definition** (from schema explorer) + optionally routine params. Output: `SpVerb { Get, Post }`.

> Nuance flagged in `sp_verb_problem.md` level B (ScriptDom/AST) is **out of scope** — the user explicitly chose the literal tail-rule. Documented as a later hardening option.

### 3.4 Verb exposed via metadata (per Q2)

`DynamicApiObjectMetadataDto` gains:
```csharp
public string? Verb { get; set; }                    // "GET" | "POST" for SP, "GET" for View/Function, null for Table
public List<RoutineParameterDto> Parameters { get; set; } = new();
```
New DTO `RoutineParameterDto { Name, DataType, ParameterMode ("IN"/"OUT"/"INOUT"), HasDefault, DefaultValue?, OrdinalPosition }`.

`DynamicApiMetadataService.GetObjectMetadataAsync` fills these:
- Table → `Verb = null`, `Parameters = []`
- View → `Verb = "GET"`, `Parameters` resolved like Functions (from the view definition / schema explorer) so they can be passed as query-string inputs
- Function/SP → resolve routine definition **and** parameters (new resolver helpers → schema explorers or direct metadata SQL), then `SpVerbClassifier.Evaluate(definition)` (SP only)

Where parameters come from:
- SQL Server: `sys.parameters` (+ `sys.types` for type name) with `is_output`, `has_default_value`, `default_value`
- MySQL: `information_schema.PARAMETERS` (PARAMETER_MODE = IN/OUT/INOUT, PARAMETER_NAME, DTD_IDENTIFIER)

### 3.5 SP/Function execution (backend, per Q3/Q4)

New service method `ExecuteRoutineAsync` + new response DTO:
```csharp
public class DynamicApiExecutionResponse
{
    public List<Dictionary<string, object?>>? ResultSets { get; set; }  // all result sets (NextResult loop)
    public Dictionary<string, object?>? OutputParams { get; set; }     // OUT / INOUT params read back
    public int RowsAffected { get; set; }
}
```

Implementation per engine (in each concrete service, via `ExecuteRoutineInternalAsync`):
- **SQL Server SP/function**: `EXEC [schema].[proc] @p1 = @in1, @p2 = @in2 OUTPUT ...` (or `SELECT dbo.fn(@p1)` for scalar, `SELECT * FROM dbo.fn(@p1, ...)` for table-valued `fn`), using `DbParameter` with `Direction.Output` for OUT/INOUT, iterate `reader.NextResultAsync`.
- **MySQL SP/function**: `CALL proc(@p1, @p2, ...)` with output params declared `Direction.Output`/`InputOutput`; scalar function `SELECT fn(@p)`. Use `reader.RecordsAffected` for `RowsAffected`.

The controller decides parameter source by HTTP verb:
- **GET → query string** → each `?param=value` maps to an IN parameter by **parameter name**. Applies to View, Function, and GET-classified SP.
- **POST → JSON body** `{ "param": value }` → IN parameters; matched by name; missing optional params simply omitted (DB default applies); missing required params → 400 with the missing names listed.

For **views** the parameters are bound as `WHERE` conditions on the underlying `SELECT ... FROM [schema].[view]` (each query-string param filters by the matching column), not an `EXEC`/`CALL`.

### 3.6 Controller changes (`BE/AutoApiEngine.Presentation/Controllers/DynamicApiController.cs`)

- `GET api/{ws}/{objectName}` (`GetList`) and `POST api/{ws}/{objectName}` (`Create`) now **branch on object type** (via `_metadataService`, cheap cache after first call):
  - Table → current behavior
  - View → list; bind query-string params as `WHERE` conditions on the view
  - SP → if classified **GET**: bind query params → `ExecuteRoutineAsync`; SP GET routes hit `GetList`, so branch there
  - SP → if classified **POST**: bind body → `ExecuteRoutineAsync` (route hits `Create`)
  - Function → always `ExecuteRoutineAsync`
- Guard rails: if the incoming verb does **not** match the classified verb (e.g., user POSTs to a GET-only SP), return `405 Method Not Allowed` with a clear message.
- Unauthorized/authorization flow untouched.

### 3.7 Frontend changes

**`FE/src/app/dashboard/api-generated/dynamic-api.service.ts`**
- Extend `ObjectMetadata` with `verb?: 'GET'|'POST'` and `parameters: RoutineParameter[]`.
- New `RoutineParameter` interface.
- Add execution calls:
  - `executeRoutineGet(workspaceId, objectName, params)` → `http.get<DynamicApiExecutionResponse>(...)`
  - `executeRoutinePost(workspaceId, objectName, body)` → `http.post<DynamicApiExecutionResponse>(...)`

**`FE/src/app/dashboard/api-generated/api-generated.ts`** — `generatedUrls` computed branches on `objectType`:
- Table → current 5 URLs
- View → 1 GET URL (list) with query string built from `parameters`; no `/{id}`
- Function → 1 GET URL (run), query string built from `parameters`
- SP → 1 URL only: `GET` if `verb === 'GET'`, `POST` if `verb === 'POST'`; no `/{id}`
- Build the GET query string from `parameters` (or leave `?param=value` placeholders); build POST body template from `parameters` (IN params only, example values via existing `generateExampleValue`).

**`FE/src/app/dashboard/api-generated/api-generated.html`**
- For SP/Function/parametrized View: show parameter inputs + a **Run** button and a response viewer (tabs/sections for `resultSets`, `outputParams`, `rowsAffected`), instead of just URL-copy rows.
- Reuse existing copy-button for tables/views without params.

---

## 4. Implementation steps (in order)

| # | Step | Files |
|---|---|---|
| 1 | Add `RoutineParameterDto`, `DynamicApiExecutionResponse`, extend `DynamicApiObjectMetadataDto` (+ `Verb`, `Parameters`) | `ServiceAbstraction/DTO/` |
| 2 | Add `ExecuteRoutineAsync` to `IDynamicApiService`; add `SpVerb` enum + `SpVerbClassifier` | `ServiceAbstraction/IDynamicApiService.cs`, `Domain/Enums/SpVerb.cs`, `Services/.../DynamicApiService/SpVerbClassifier.cs` |
| 3 | Create `DynamicApiServiceBase` with all shared code from the old class | new file |
| 4 | Create `SqlServerDynamicApiService`, `MySqlDynamicApiService` (dialect hooks + SP/function execution) | 2 new files |
| 5 | Create `DynamicApiServiceResolver` (dispatch + internal metadata helpers) | new file |
| 6 | Delete old `DynamicApiService.cs`; rewire `DynamicApiMetadataService` and `ServiceCollectionExtensions` DI | `DatabaseManagementServices/DynamicApiService.cs`, `DynamicApiMetadataService.cs`, `Providers/ServiceCollectionExtensions.cs` |
| 7 | Add routine definition + parameter metadata to resolver/metadata service; wire `Verb` + `Parameters` into `GetObjectMetadataAsync` | `DynamicApiMetadataService.cs` + resolver |
| 8 | Update `DynamicApiController` (type branching, 405 guard, param binding) | `Presentation/Controllers/DynamicApiController.cs` |
| 9 | Frontend: extend service DTOs + execution calls | `FE/.../api-generated/dynamic-api.service.ts` |
| 10 | Frontend: `generatedUrls` branching + param/run UI | `FE/.../api-generated/api-generated.ts`, `.html` |
| 11 | Build, test, verify | see §5 |

Each step keeps the solution compiling (`dotnet build` after each) before the next.

---

## 5. Verification

- Backend: `dotnet build BE/AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj`
- Frontend: `npm run build` from `FE/`
- Manual checks (SQL Server + MySQL both):
  - Table object → still 5 endpoints, CRUD works (regression)
  - View object → 1 GET endpoint (list); parametrized view → `?param=value` filters the result (including PK lookup)
  - GEt-classified SP (ends with `SELECT ...`) → 1 GET URL; `?param=value` executes and returns `resultSets`
  - POST-classified SP (contains `INSERT/UPDATE/DELETE`, or ends with exec-without-select, or mixed) → 1 POST URL; body executes; `outputParams`/`rowsAffected` populated for OUT params
  - SP with no params → Run with `{}` / empty query
  - Function → 1 GET URL, runs and returns scalar/table
  - Wrong verb on an SP → **405**

---

## 6. Risks / notes

- **Tail-rule precision**: `SELECT` detection is heuristic (as specified); `BEGIN...END` wrapper handling and comment stripping are the two likely edge cases — covered in §3.3 step 1–2. If a real proc gets misclassified, POST default (rule 3) is the safe fallback.
- **Workspace double-load**: resolver loads the workspace once and passes it down (interface wrappers keep the old signature) → no extra DB round-trip per request.
- **Route conflicts**: SP GET goes through the existing `GetList` route (branch inside); no new route segments are introduced, so frontend URLs stay stable.
- **MySQL `CALL` output params / multiple result sets** are supported by `MySqlConnector`'s `MySqlCommand` — verify behavior for mixed `OUT` + result-set procs during testing.
- SQLite/PostgreSQL later = new `SqliteDynamicApiService` / `PostgreSqlDynamicApiService` + resolver arms; interface/base unchanged (Open/Closed achieved).