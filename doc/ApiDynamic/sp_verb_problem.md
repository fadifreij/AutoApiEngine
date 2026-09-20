# Stored Procedure HTTP Verb Problem — Design Suggestion

> Design consultation only. No code was changed as part of this analysis.
> Date: 2026-09-19

## Context / Current State

The **Generated API** section exposes the same 5 endpoints for every database object
(Table, View, StoredProcedure, Function):

- `GET  /api/{ws}/{object}`            — list
- `GET  /api/{ws}/{object}/{id}`       — by primary key
- `POST /api/{ws}/{object}`            — create
- `PUT  /api/{ws}/{object}/{id}`       — update by primary key
- `DELETE /api/{ws}/{object}/{id}`     — delete by primary key

Key finding: **stored procedures don't actually work through the 4-verb model today.**
`DynamicApiService` builds real SQL (`SELECT ... FROM [schema].[obj]`,
`INSERT INTO ...`) against the object name on all endpoints, and the frontend
`generatedUrls` always renders the same 5 URLs for every object type. An SP would fail
under `SELECT ... FROM`. The question addressed here is exactly that design gap.

---

## 1. Core idea: an SP is an operation, not a resource

The 4-verb CRUD model (GET/POST/PUT/DELETE) maps cleanly to *"a row in a table"*.
A stored procedure is a **callable operation**, and REST has only two verbs that
legitimately apply to an operation:

| Verb | Meaning | Applies to SP? |
|------|---------|----------------|
| **GET** | Safe, side-effect-free read | Only if the SP is *proven* read-only |
| **POST** | Execute something that may have side effects, can return a result body | **Always** — it's the catch-all for operations |
| **PUT** | Idempotent full replace of a resource | **Never** — re-executing an SP is usually *not* idempotent (e.g., re-INSERT duplicates), so calling it PUT breaks HTTP semantics |
| **DELETE** | Remove a resource | **Never** — an SP doesn't guarantee the resource is deleted at a stable URI |

**Headline suggestion:**

- **SELECT-only SP → `GET` only**
- **INSERT / UPDATE / DELETE / MERGE / mixed / unknown → `POST` only**
- **No PUT, no DELETE, and no `/{id}` route for any SP** (an SP has no natural primary
  key, so the "get by id" / "update by id" / "delete by id" URLs disappear too)

Side effect: SPs show **1** URL in the UI (or 2 max if a read proc also allows POST)
instead of 5. Tables/views keep the current 5. The frontend `generatedUrls`
computation just branches on `objectType === 'StoredProcedure'`.

---

## 2. The "mixed" case — the rule that removes the guesswork

**Classification is one-directional:**

- *"This SP contains INSERT/UPDATE/DELETE/MERGE/TRUNCATE/EXEC-of-another-proc/dynamic
  SQL"* → **provable and reliable.** If found → **POST**.
- *"No writes found, therefore read-only"* → **NOT provable.** The proc could hide a
  write in dynamic SQL (`EXEC('INSERT INTO ...')`) or call another proc that writes.

So the safe design is: **an SP is classified GET only when the analysis can be
*positive* about read-only; in every other case (mixed, write, unparseable, unknown)
it is POST.**

| SP body contains | Exposed actions |
|------------------|-----------------|
| Only pure SELECTs, no `EXEC`, no dynamic SQL | **GET** (optionally also POST) |
| Any INSERT/UPDATE/DELETE/MERGE/TRUNCATE/temp-table-write, or `EXEC` of an unknown proc | **POST** |
| Mixed read + write | **POST** |
| Dynamic SQL (`EXEC('...')`, `sp_executesql`) | **POST** |
| Parsing fails / can't tell | **POST** (conservative default) |

This mirrors the safety direction already used in
`DatabaseToolService.ExecuteReadOnlyQueryAsync` — it proves writes to reject them,
never the reverse.

---

## 3. How to classify (3 levels, pick per appetite)

### Level A — Metadata only, zero parsing (fastest to ship)
Use a naming convention that opts into GET: `Get*`, `Select*`, `qry_*`, or a
configurable prefix list → GET candidate; everything else → POST. Deterministic,
never falsely marks a writing proc as read-only. Worst case a read proc gets POST —
still correct and safe.

### Level B — Metadata + body scan (better, medium effort)
The body is already available via `RoutineDefinitionDto.Definition` (returned by
`SqlSchemaExplorer.GetRoutineDefinitionsAsync` / `MySqlSchemaExplorer`). Add a
classifier that:

1. Strips comments and string literals — ideally using the real T-SQL parser NuGet
   **`Microsoft.SqlServer.TransactSql.ScriptDom`** for SQL Server (built-in parser
   for MySQL) to build a proper AST;
2. Walks the top-level statements looking for write statements;
3. Emits a `spReadMode`: `ReadOnly | Write | Mixed | Unknown` for metadata/UI.

Plus a **result-set check** for SQL Server:
`sys.dm_exec_describe_first_result_set(N'EXEC schema.proc', NULL, 0)` returns the
first result set's columns. No columns → returns no rows → clearly a *command*, not a
query. That's authoritative metadata, not parsing.

### Level C — Human override (most robust, recommended regardless)
When an SP is selected in the **Generated API UI**, show the detected value with an
override control:

> Actions: ○ Get only   ● Post only   ○ Both (tooltip: "Post is always available;
> Get only if no writes inside")

Store the choice at generation time. The system proposes, the developer disposes.
This is what real API generators (Hasura, PostgREST, OData function imports) do for
SQL functions/procs.

---

## 4. Input parameters & the "no input" case

- **Input params →** for GET they map to **query string** params; for POST they map
  to **JSON body properties** (typed from `sys.parameters` /
  `information_schema.parameters`, respecting `OUT` / `INOUT` mode). A single SP
  endpoint is cleaner than a path with `{id}` because an SP has no path-level key.
- **Optional params** (SQL Server defaults) → declared optional in the generated
  OpenAPI / UI; omitted params are simply not sent (SQL Server applies the default).
  Never required unless metadata says `has_default_value = false`.
- **No-input SPs →** nothing special, but the UI should treat them as a **"Run"
  action** — endpoint with empty body / empty query and a Run button, instead of
  showing nothing. The no-param case just renders the call with `{}`.
- **Output / return params →** surface in the **response body** as a top-level field
  (e.g., `{ "outputParams": {...}, "resultSets": [...], "rowsAffected": n }`), since
  the dynamic result set shape can't be known before execution.

---

## 5. One endpoint to rule them all (summary mapping)

| Object type | Actions exposed |
|---|---|
| Table | GET list, GET by id, POST, PUT, DELETE *(unchanged)* |
| View | GET list, GET by id *(read-only data source — could also hide PUT/DELETE, same argument)* |
| StoredProcedure | `GET` *only if proven read-only* · `POST` *always* · no `/{id}`, no PUT/DELETE |
| Function | `GET` (scalar from function) — like read-only SPs |

---

## Single strongest recommendation

**Default every SP to POST; only expose GET when the classifier can positively prove
read-only (naming convention + body scan), and always let the developer override in
the UI.** Never map a mixed/insert proc to PUT or DELETE — a PUT with non-idempotent
re-execution is a violation waiting to happen, and the "mixed → POST" rule forever
removes the "I don't know what the action should be" question.