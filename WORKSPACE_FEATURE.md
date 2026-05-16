# Workspace Feature

This file documents everything about the workspace feature - the core value proposition of AutoApiEngine.

---

## Data Flow

```
User fills form (WorkspaceNew)
    |
    v
POST /api/workspaces { CreateWorkspaceDto }
    |
    v
WorkspaceController.Create()
    |-- validates org exists
    |-- parses DatabaseEngine from string
    |-- creates Workspace entity
    |-- workspaceRepository.AddAsync()
    |
    v
Database
    |-- Workspaces table
    |-- Per-workspace database (NOT YET IMPLEMENTED - the actual provisioning of
    |   a dedicated database per workspace is not wired up)
```

## Workspace Entity

| Field | UI Control | Notes |
|-------|-----------|-------|
| Name | Text input | Required |
| EncryptionKey | Password input w/ eye toggle | Optional. **Never used for actual encryption** |
| DatabaseEngine | Select dropdown | SqlServer/PostgreSql/MySql/Sqlite |
| DbUserName | Text input (external mode) | Optional |
| DbPassword | Password input (external mode) | Optional |
| DatabaseName | Text input (external mode) | Optional |
| OrganizationId | (from auth context) | Currently sent from client, not derived from JWT |

---

## Frontend: WorkspaceNew Component

**File:** `FE/src/app/dashboard/workspace-new/workspace-new.ts`

### Current State: UI Mockup Only

```typescript
// The ONLY logic in the component:
dbType = signal<'hosted' | 'external'>('hosted');
showEncryptionKey = signal(false);
```

### UI Sections

1. **Workspace Name** - text input (unbound)
2. **Encryption Key** - password input with eye toggle (unbound, always visible)
3. **Database Type Toggle** - two selectable cards:
   - **Hosted by Us** - shows DB engine select + "No credentials needed" info
   - **External Database** - shows DB engine select + Host + Username + Password + "Test Connection" button
4. **Cancel** link (routerLink to `/app/dashboard`)
5. **Create Workspace** button (no action bound)

### What's Missing (Functional Gap)

- No `ngModel` bindings on any form field
- No form submission handler (`(ngSubmit)` or button click handler)
- No HTTP service call to `POST /api/workspaces`
- No model/interface for workspace data on frontend
- No form validation
- No error handling or loading states

---

## Backend: WorkspaceController

**File:** `BE/AutoApiEngine.Presentation/Controllers/WorkspaceController.cs`

### Create Endpoint (POST)

```csharp
// Creates Workspace entity with provided fields
// Does NOT:
// - Provision an actual database
// - Validate connection to external DB
// - Use EncryptionKey for anything
// - Derive OrganizationId from JWT claim (accepts from body)
```

### Update Endpoint (PUT)

```csharp
// Updates all fields including EncryptionKey
// Workspace must exist
```

---

## The Encryption Key Question

The requirement states:
- **External database:** encryption key should be **disabled** (not shown or not required)
- **Hosted by us:** encryption key should be **enabled** (shown and configurable)

### Current Implementation
- Encryption key field is **always visible** regardless of dbType
- Field has no conditional display logic tied to `dbType()` signal
- No binding means it's dead UI regardless

### What Needs To Change
- When `dbType() === 'external'`: hide encryption key field (or disable it)
- When `dbType() === 'hosted'`: show encryption key field
- Backend should also respect this: when `DatabaseEngine` indicates external, ignore EncryptionKey

---

## Future Work Required

1. Wire ngModel bindings on WorkspaceNew form
2. Create workspace model interface on FE
3. Add workspace service with HTTP calls
4. Add form validation (required fields per mode)
5. Add conditional display for encryption key (hosted vs external)
6. Add `Test Connection` button logic for external DB
7. Implement actual per-workspace database provisioning on BE
8. Implement actual encryption/decryption using EncryptionKey
9. Derive OrganizationId from JWT instead of client body
