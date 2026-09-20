# Dynamic Generic API — Architecture & Implementation Plan

## 1. Overview

Create a **zero-code generic API layer** that exposes any database table, view, stored procedure, or function as RESTful endpoints with filtering, paging, column selection, and related-table expansion — all discovered dynamically from the database schema.

---

## 2. URL Structure

All endpoints sit under a single controller:

```
/api/{workspaceId}/{objectName}
/api/{workspaceId}/{objectName}/{id}
```

> The `{objectName}` is the actual database object name (table, view, SP, or function). No persistence layer — the API is fully stateless; every request carries its full configuration via query parameters.

| Method | URL | Purpose |
|--------|-----|---------|
| `GET` | `/api/{ws}/{table}` | List records with filter/paging/select/include |
| `GET` | `/api/{ws}/{table}/{id}` | Get one record by PK |
| `POST` | `/api/{ws}/{table}` | Insert a record |
| `PUT` | `/api/{ws}/{table}/{id}` | Update a record by PK |
| `DELETE` | `/api/{ws}/{table}/{id}` | Delete a record by PK |
| `POST` | `/api/{ws}/{sp}` | Execute a stored procedure |
| `GET` | `/api/{ws}/{view}` | Query a view (same as table GET) |
| `GET` | `/api/{ws}/{fn}?params=...` | Execute a table-valued function |

> `{ws}` = workspace GUID. `{objectName}` = table/view/sp/function name. The API auto-detects the object type by querying `INFORMATION_SCHEMA`.

---

## 3. Query Parameter Design

### 3.1 Column Selection — `select`

```
GET /api/{ws}/employees?select=id,firstName,lastName,email
```

- Comma-separated column names.
- If omitted → `SELECT *` (all columns).
- Supports **dot-notation for related tables** via foreign keys:

```
GET /api/{ws}/employees?select=id,firstName,department.name,department.budget
```

This auto-detects the FK `department_id → departments.id` and builds a JOIN.

### 3.2 Include Related Tables — `include`

```
GET /api/{ws}/departments?include=employees(id,firstName,lastName),manager(id,name)
```

- `include=TableName(col1,col2,…)` — the FK is auto-resolved from the target table back to the source.
- If no columns specified → all columns from related table.
- Supports nested includes: `include=employees.orders(id,total)`

### 3.3 Filtering — `filter`

**Shorthand (eq):**
```
GET /api/{ws}/employees?departmentId=5&isActive=true
```

**Explicit operators:**
```
GET /api/{ws}/employees?filter=age:gte:25&filter=name:contains:john
```

| Operator | Meaning | Example |
|----------|---------|---------|
| `eq` | Equals | `status:eq:active` |
| `neq` | Not equals | `status:neq:archived` |
| `gt` | Greater than | `age:gt:18` |
| `gte` | ≥ | `age:gte:21` |
| `lt` | Less than | `price:lt:100` |
| `lte` | ≤ | `price:lte:50` |
| `contains` | LIKE '%val%' | `name:contains:john` |
| `startswith` | LIKE 'val%' | `code:startswith:AA` |
| `endswith` | LIKE '%val' | `email:endswith:.com` |
| `in` | IN (csv) | `id:in:1,2,3` |
| `isnull` | IS NULL | `deletedAt:isnull:true` |
| `isnotnull` | IS NOT NULL | `email:isnotnull:true` |

**Combined with `and`/`or`:**
```
GET /api/{ws}/employees?filter=age:gte:25&filter=departmentId:eq:5
```
All filters are AND-ed by default. An `or` prefix can be used:
```
GET /api/{ws}/employees?filter_or=name:contains:john&filter_or=name:contains:jane
```

### 3.4 Paging — `page` / `pageSize`

```
GET /api/{ws}/employees?page=1&pageSize=20
GET /api/{ws}/employees?page=2&pageSize=50
```

- `pageSize` defaults to **100**, max **1000**.
- Response includes `X-Total-Count` header and a `totalCount` field in body.
- If `pageSize=0` → return all records (no limit, use with caution).

### 3.5 Sorting — `sort`

```
GET /api/{ws}/employees?sort=lastName:asc,firstName:asc
GET /api/{ws}/employees?sort=createdAt:desc
```

- Default sort is by primary key ascending.

### 3.7 Composite Primary Keys

For tables with composite PKs:
- **GET by ID** (single PK): `/api/{ws}/{table}/{id}`
- **GET by composite PK**: `/api/{ws}/{table}?pk1=v1&pk2=v2` (query params)
- **PUT by composite PK**: `PUT /api/{ws}/{table}?pk1=v1&pk2=v2` with body
- **DELETE by composite PK**: `DELETE /api/{ws}/{table}?pk1=v1&pk2=v2`

The API detects the PK columns from INFORMATION_SCHEMA and routes accordingly.

### 3.6 Full Example

```
GET /api/{ws}/employees?select=id,firstName,lastName,department.name&
  include=orders(id,total)&
  filter=age:gte:25&
  filter=isActive:eq:true&
  sort=lastName:asc&
  page=1&pageSize=20
```

---

## 4. Response Format

### Success (GET list)

```json
{
  "data": [
    {
      "id": 1,
      "firstName": "John",
      "lastName": "Doe",
      "department": { "name": "Engineering" },
      "orders": [
        { "id": 101, "total": 250.00 },
        { "id": 102, "total": 99.99 }
      ]
    }
  ],
  "paging": {
    "page": 1,
    "pageSize": 20,
    "totalCount": 156,
    "totalPages": 8
  }
}
```

### Success (single record)

```json
{
  "data": {
    "id": 1,
    "firstName": "John",
    "lastName": "Doe",
    "department": { "name": "Engineering" },
    "orders": [ ... ]
  }
}
```

### Success (POST/PUT)

```json
{
  "data": {
    "id": 157,
    "firstName": "Jane",
    "lastName": "Smith"
  },
  "message": "Record created successfully"
}
```

### Success (DELETE)

```json
{
  "message": "Record deleted successfully",
  "id": 157
}
```

### Error

```json
{
  "error": "Column 'unknown_column' does not exist on table 'employees'",
  "code": "INVALID_COLUMN",
  "statusCode": 400
}
```

---

## 5. Backend Architecture

### 5.1 New Files Needed

| Layer | File | Purpose |
|-------|------|---------|
| **ServiceAbstraction** | `IDynamicApiService.cs` | Interface |
| **ServiceAbstraction/DTO** | `DynamicApiRequest.cs` | Request DTO for query params |
| **ServiceAbstraction/DTO** | `DynamicApiResponse.cs` | Response DTO |
| **ServiceAbstraction/DTO** | `ForeignKeyInfoDto.cs` | FK relationship info |
| **ServiceAbstraction** | `IForeignKeyService.cs` | FK discovery interface |
| **Services** | `DynamicApiService.cs` | Core implementation (SQL builder) |
| **Services** | `SqlForeignKeyService.cs` | SQL Server FK discovery |
| **Services** | `MySqlForeignKeyService.cs` | MySQL FK discovery |
| **Services** | `ForeignKeyServiceResolver.cs` | Strategy resolver |
| **Presentation** | `DynamicApiController.cs` | Controller |
| **ServiceAbstraction** | `IDynamicApiMetadataService.cs` | Schema metadata for UI |
| **Services** | `DynamicApiMetadataService.cs` | Metadata impl |

### 5.2 Core SQL Builder Logic (`DynamicApiService`)

**Step 1: Validate & resolve object**
- Check `INFORMATION_SCHEMA.TABLES` / `VIEWS` / `ROUTINES` for the name.
- Get primary key column(s) for the table.
- Get all valid column names.

**Step 2: Build SELECT clause**
- Parse `select` parameter.
- Validate each column exists on the table.
- For dot-notation (e.g., `department.name`):
  1. Look up FK columns on the source table.
  2. Identify the referenced table (`departments`).
  3. Add LEFT JOIN.
  4. Qualify the column as `[alias].[column]`.

**Step 3: Process `include` parameter**
- Parse `TableName(col1,col2)` format.
- Look up FK from the source table to the target table.
- Add LEFT JOIN with table alias.
- Nest JSON results using `FOR JSON PATH` (SQL Server) or `JSON_ARRAYAGG` (MySQL 8+).

**For SQL Server** — use nested `FOR JSON PATH` queries:
```sql
SELECT e.id, e.firstName, e.lastName,
  (SELECT id, total FROM orders WHERE orders.employee_id = e.id FOR JSON PATH) AS orders
FROM employees e
WHERE e.department_id = 5
ORDER BY e.lastName ASC
OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
```

**For MySQL**:
```sql
SELECT e.id, e.firstName, e.lastName,
  (SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'total', total))
   FROM orders WHERE orders.employee_id = e.id) AS orders
FROM employees e
WHERE e.department_id = 5
ORDER BY e.lastName ASC
LIMIT 20 OFFSET 0
```

**Step 4: Build WHERE clause**
- Parse each `filter` parameter.
- Validate column names and data types for operator compatibility.
- Use parameterized queries to prevent SQL injection.

**Step 5: Add paging**
- SQL Server: `OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY`
- MySQL: `LIMIT @pageSize OFFSET @offset`
- Run a `COUNT(*)` query first (with same WHERE but no SELECT/ORDER/OFFSET/FETCH) for total.

**Step 6: Execute & return**

### 5.3 Security

| Concern | Mitigation |
|---------|-----------|
| **SQL injection (table/col names)** | Validate against `INFORMATION_SCHEMA` — reject unknown identifiers |
| **SQL injection (values)** | Always use parameterized queries (`SqlParameter`) |
| **Unauthorized workspace access** | Verify user owns the workspace via existing auth |
| **DDL / destructive SQL** | DynamicApiService only generates DML (SELECT/INSERT/UPDATE/DELETE) — never raw SQL passthrough |
| **Cross-schema access** | Default to `dbo` schema; optionally support schema-qualified names `schema.table` |

### 5.4 POST / PUT / DELETE Logic

**POST:**
```sql
INSERT INTO [dbo].[employees] (first_name, last_name, department_id)
OUTPUT INSERTED.*
VALUES (@firstName, @lastName, @departmentId)
```

- Extract column→value pairs from request body JSON.
- Validate all columns exist on the table.
- For PUT with missing optional columns → keep existing (partial update / PATCH semantics).

**PUT:**
```sql
UPDATE [dbo].[employees]
SET first_name = @firstName, last_name = @lastName
OUTPUT INSERTED.*
WHERE id = @id
```

**DELETE:**
```sql
DELETE FROM [dbo].[employees]
OUTPUT DELETED.*
WHERE id = @id
```

- Must cascade-delete or fail based on FK constraints (natural DB behavior).
- Support composite primary keys: `DELETE ... WHERE pk1=@v1 AND pk2=@v2`

### 5.5 Stored Procedures & Functions

**Stored Procedures (read-only detection):**
- **Read-only SPs** (detected by checking if SP starts with SELECT or has no INSERT/UPDATE/DELETE in its body):
  ```
  GET /api/{ws}/usp_GetEmployeeReport?departmentId=5&includeInactive=false
  ```
- **Mutating SPs** (contain INSERT/UPDATE/DELETE):
  ```
  POST /api/{ws}/usp_UpdateEmployeeStatus
  Body: { "employeeId": 5, "status": "active" }
  ```
- Detection logic: query `sys.sql_modules` (SQL Server) or `ROUTINE_DEFINITION` (MySQL) and check for write keywords. If ambiguous → default to POST.
- Maps body properties to SP parameters.
- Uses `EXEC usp_GetEmployeeReport @departmentId=5, @includeInactive=0`.
- Returns result set(s).

**Table-Valued Functions (GET):**
```
GET /api/{ws}/fn_GetEmployeesByDept?departmentId=5
```
- Maps query params to function parameters.
- Uses `SELECT * FROM fn_GetEmployeesByDept(@departmentId)`.
- Supports `select`, `filter`, `sort`, `page` on the result.

---

## 6. Foreign Key Discovery Service

### SQL Server Query

```sql
SELECT
  fk.name AS FK_Name,
  COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS ColumnName,
  OBJECT_SCHEMA_NAME(fkc.referenced_object_id) AS RefSchema,
  OBJECT_NAME(fkc.referenced_object_id) AS RefTable,
  COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS RefColumn
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc
  ON fkc.constraint_object_id = fk.object_id
WHERE fk.parent_object_id = OBJECT_ID(@tableName)
```

### Output DTO

```json
{
  "tableName": "employees",
  "foreignKeys": [
    {
      "fkName": "FK_employees_department",
      "column": "department_id",
      "referencedTable": "departments",
      "referencedColumn": "id",
      "referencedSchema": "dbo"
    }
  ],
  "referencedBy": [
    {
      "fkName": "FK_orders_employee",
      "table": "orders",
      "column": "employee_id",
      "referencedColumn": "id"
    }
  ]
}
```

This service already partially exists in `SqlSchemaExplorer.GetConstraintDefinitionsAsync`. We extract it into a dedicated service with both directions (FKs FROM this table, and tables that reference this table).

---

## 7. Frontend UI Plan

### 7.1 Where it lives

Replace the existing static `api-generated` component mockup at:
```
FE/src/app/dashboard/api-generated/
```

### 7.2 UI Layout

```
┌─────────────────────────────────────────────────────────────┐
│  Dynamic API Explorer                                       │
├─────────────────────────────────────────────────────────────┤
│  Select Database Object ───────────────────────────────┐   │
│  ┌──────────────────────────────────────────────────┐  │   │
│  │ [database objects dropdown/search]               │  │   │
│  │ ┌──────────────────────────────────────────────┐ │  │   │
│  │ │ 📄 employees          (Table, 12 cols)      │ │  │   │
│  │ │ 📄 departments        (Table, 5 cols)       │ │  │   │
│  │ │ 👁️ vw_employee_full  (View)                │ │  │   │
│  │ │ ⚙️ usp_GetReport     (Procedure)           │ │  │   │
│  │ │ 𝘧 fn_GetByDept       (Function)            │ │  │   │
│  │ └──────────────────────────────────────────────┘ │  │   │
│  └──────────────────────────────────────────────────┘  │   │
├─────────────────────────────────────────────────────────────┤
│  Columns & Includes ─────────────────────────────────────── │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Columns from "employees":                              │ │
│  │ ☑ id (PK)      ☑ first_name    ☑ last_name            │ │
│  │ ☑ email        ☑ phone         ☑ salary               │ │
│  │ ☑ department_id  ☑ hire_date   ☑ is_active            │ │
│  │ ☑ created_at   ☐ updated_at                           │ │
│  │                                                         │ │
│  │ ─── Related Tables (via FK) ───                        │ │
│  │ ☑ Include "departments"  [name budget]  ⚙️            │ │
│  │ ☐ Include "orders"       [id total status] ⚙️         │ │
│  │                                                         │ │
│  │ Tables that reference this:                             │ │
│  │ ☐ "orders" has FK to employees [id total status] ⚙️   │ │
│  └────────────────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────────────────┤
│  Filters ────────────────────────────────────────────────── │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ [+ Add Filter]                                         │ │
│  │ ┌──────────┬──────────┬──────────────────────────────┐ │ │
│  │ │ Column   │ Operator │ Value                        │ │
│  │ │ [salary] │ [gte]  ▾ │ [50000]                      │ │
│  │ │ [AND]                                                │ │
│  │ │ [dept_id]│ [eq]   ▾ │ [3]                          │ │
│  │ └──────────┴──────────┴──────────────────────────────┘ │ │
│  └────────────────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────────────────┤
│  Paging & Sorting ───────────────────────────────────────── │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Page Size: [20 ▾]  Max: 1000                          │ │
│  │ Sort By: [last_name ▾] [ASC ▾] [+ Add Sort]           │ │
│  └────────────────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────────────────┤
│  Generated Endpoints ────────────────────────────────────── │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ 🔵 GET    /api/{ws}/employees?select=...      │ │
│  │ 🟢 POST   /api/{ws}/employees                 │ │
│  │ 🟡 PUT    /api/{ws}/employees/{id}            │ │
│  │ 🔴 DELETE /api/{ws}/employees/{id}            │ │
│  │                                                         │ │
│  │ [Copy Endpoint]  [Try in Studio]  [View Docs]         │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  [Copy Endpoint URL]                                         │
└─────────────────────────────────────────────────────────────┘
```

### 7.3 UI Components

| Component | File | Description |
|-----------|------|-------------|
| `ObjectSelector` | `object-selector.ts` | Searchable dropdown/tree of all DB objects with type icons |
| `ColumnSelector` | `column-selector.ts` | Checkbox list of columns with FK-aware "Include Related" |
| `FilterBuilder` | `filter-builder.ts` | Dynamic row-based filter builder (column→operator→value) with AND/OR grouping |
| `SortBuilder` | `sort-builder.ts` | Add/remove sort columns with ASC/DESC |
| `PagingConfig` | `paging-config.ts` | Page size slider/input |
| `EndpointPreview` | `endpoint-preview.ts` | Live preview of generated endpoint URL and example response |
| `ApiGenerated` | `api-generated.ts` | Parent orchestrator component |

### 7.4 Frontend Service

**`DynamicApiService`** (`dynamic-api.service.ts`):
- `GET /api/schema/{workspaceId}/explore` — list all objects (already exists via `SchemaExplorerController`)
- `GET /api/schema/{workspaceId}/foreign-keys?table={name}` — get FK info for a table
- `GET /api/schema/{workspaceId}/columns?table={name}` — get column list for a table


### 7.5 Data Flow

```
1. User selects workspace → workspace loaded into WorkspaceStateService
2. api-generated component loads → calls SchemaExplorer for object list
3. User picks a table → calls FK service → shows columns + related tables
4. User configures selects, includes, filters, paging in UI
5. Live preview updates URL and example in real-time
6. User copies the generated URL or calls it directly from the UI
```

### 7.6 No Persistence — Stateless API

The API is **fully stateless**. There is no deployment/persistence step. The user:
1. Selects a table in the UI
2. Configures columns, filters, paging
3. **Copies the generated URL** or uses it directly
4. Every request carries its full configuration via query parameters

This keeps the implementation simple and the API stateless/cacheable.

---

## 8. Implementation Order (Step-by-Step)

### Phase 1: Backend Core (Week 1)

| Step | File(s) | Description |
|------|---------|-------------|
| 1 | `ForeignKeyInfoDto.cs`, `IForeignKeyService.cs` | FK discovery interface + DTO |
| 2 | `SqlForeignKeyService.cs` | SQL Server impl — query sys.foreign_keys |
| 3 | `MySqlForeignKeyService.cs` | MySQL impl — query INFORMATION_SCHEMA.KEY_COLUMN_USAGE |
| 4 | `ForeignKeyServiceResolver.cs` | Strategy resolver (follows existing pattern) |
| 5 | Register services in `ServiceCollectionExtensions.cs` | DI registration |
| 6 | `DynamicApiRequest.cs` | Request DTO (select, include, filter, sort, page, pageSize) |
| 7 | `DynamicApiResponse.cs` | Response DTO (data, paging, error) |
| 8 | `IDynamicApiService.cs` | Interface with Get/Create/Update/Delete |
| 9 | `DynamicApiService.cs` | Core SQL builder (SELECT, JOIN, WHERE, paging, INSERT, UPDATE, DELETE) |
| 10 | `DynamicApiController.cs` | 5 endpoints (GET list, GET by id, POST, PUT, DELETE) |
| 11 | Build & verify | `dotnet build` zero errors |

### Phase 2: Backend Advanced Features (Week 2)

| Step | File(s) | Description |
|------|---------|-------------|
| 12 | `DynamicApiService.cs` — includes | Nested JSON queries for related tables |
| 13 | `DynamicApiService.cs` — sp/fn | Stored procedure + function support |
| 14 | `DynamicApiService.cs` — complex filtering | `in`, `isnull`, `or` logic |
| 15 | SQL injection audit | Validate all identifiers against INFORMATION_SCHEMA |
| 16 | Workspace auth integration | Ensure user owns the workspace |
| 17 | `DynamicApiMetadataService.cs` | Metadata endpoint for UI (columns, FKs, types) |
| 18 | End-to-end test | Test with actual SQL Server DB |

### Phase 3: Frontend UI (Week 3)

| Step | File(s) | Description |
|------|---------|-------------|
| 19 | `dynamic-api.service.ts` | Angular service for all dynamic API calls |
| 20 | `object-selector.ts` | DB object search/dropdown component |
| 21 | `column-selector.ts` | Column checkboxes with FK expansion |
| 22 | `filter-builder.ts` | Dynamic filter row builder |
| 23 | `sort-builder.ts` | Sort column builder |
| 24 | `paging-config.ts` | Page size config |
| 25 | `endpoint-preview.ts` | Live URL preview + copy-to-clipboard |
| 26 | `api-generated.ts/html/scss` | Full rewrite with real data binding |
| 27 | Build & verify | `npm run build` zero errors |

---

## 10. Decisions Log (from your answers)

| Question | Decision |
|----------|----------|
| URL prefix | `/api/{workspaceId}/` (short) |
| Persistence | None — full config in query params, stateless |
| Stored procedure method | Auto-detect read-only → GET; mutating → POST |
| Auth | Skip for now (add via API keys later) |
| UI styling | Keep hand-written CSS (match existing project) |
| Composite PKs | Single PK → path param; composite PKs → query params |


---


