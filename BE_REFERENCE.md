# Backend Reference (BE/)

## Solution Structure (6 Projects)

```
BE/
  AutoApiEngine.Domain/              Leaf - entities, enums, base classes
  AutoApiEngine.Persistence/         EF Core DbContext, entity config, seeding
  AutoApiEngine.ServiceAbstraction/  Interfaces, DTOs, settings models
  AutoApiEngine.Services/            Repositories, auth, DB management implementations
  AutoApiEngine.Presentation/        Controllers, SignalR hubs, BaseController
  AutoApiEngine.ApiServices/         Entry point, Program.cs, DI registration, DB provider
```

No `.sln` file - run `dotnet` against project paths.

---

## Project Dependencies

```
ApiServices -> Persistence, Presentation, Services
Presentation -> Domain, Persistence, ServiceAbstraction, Services
Services -> Persistence, ServiceAbstraction
Persistence -> Domain
ServiceAbstraction -> Domain
Domain -> (none)
```

---

## Controllers & Endpoints

### AuthController (`api/auth`)
| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/auth/register` | Register user via Keycloak |
| POST | `/api/auth/login` | OAuth2 code exchange, sets `refresh_token` cookie |
| POST | `/api/auth/refresh-token` | Reads cookie, returns new tokens |
| POST | `/api/auth/logout` | Clears cookie, returns Keycloak logout URL |

### WorkspacesController (`api/workspaces`)
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/workspaces` | List all |
| GET | `/api/workspaces/organization/{organizationId}` | By org |
| GET | `/api/workspaces/{id}` | By ID |
| POST | `/api/workspaces` | Create |
| PUT | `/api/workspaces/{id}` | Update |
| DELETE | `/api/workspaces/{id}` | Delete |

### DatabaseOperationsController (`api/database`)
| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/database/backup` | Backup with SignalR progress |
| POST | `/api/database/restore` | Upload + restore with SignalR progress |

---

## Key Services

| Service | Interface | Role |
|---------|-----------|------|
| `AuthService` | `IAuthService` | `RegisterAsync(RegisterRequest)` |
| `KeycloakService` | (concrete) | Token exchange, refresh, logout with Keycloak |
| `SqlDatabaseManagementService` | `IDatabaseManagementService` | Backup/restore operations |
| `SqlServerConnectionStringBuilder` | `IConnectionStringBuilder` | Builds SQL Server connection strings |
| `MySqlConnectionStringBuilder` | `IConnectionStringBuilder` | Builds MySQL connection strings |

---

## Repositories

| Interface | Implementation | Extras |
|-----------|---------------|--------|
| `IGenericRepository<T>` | `GenericRepository<T>` | CRUD for any BaseEntity |
| `IWorkspaceRepository` | `WorkspaceRepository` | + `GetByOrganizationIdAsync` |
| `IOrganizationRepository` | `OrganizationRepository` | None beyond generic |

Generic repository methods: `GetAllAsync`, `GetByIdAsync`, `FindAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`

---

## BaseController Pattern

Controllers inherit `BaseController` and use `HandleRequestAsync(...)` which wraps operations in try-catch, returning standardized error responses. `WorkspacesController` uses this pattern; `AuthController` and `DatabaseOperationsController` inherit `ControllerBase` directly.

---

## Program.cs Startup Flow

1. Add controllers with JSON options (camelCase, enum strings)
2. Add CORS (AllowFrontend: `localhost:4200`, `localhost:7002`, credentials allowed)
3. Add OpenAPI
4. `AddApiServices(config)` - registers DI (settings, auth service, repositories, KeycloakService)
5. `AddDatabaseContext(databaseProvider)` - registers EF Core with SqlServer (MySql throws)
6. Map OpenAPI in dev
7. `UseCors` -> `UseAuthorization` -> `MapControllers`

**Note:** `UseAuthentication()` is NOT called. JWT Bearer packages are imported but not configured.

---

## DTOs

### CreateWorkspaceDto
Name, EncryptionKey?, DbUserName?, DbPassword?, DatabaseName?, DatabaseEngine (default "SqlServer"), OrganizationId

### UpdateWorkspaceDto
Same + IsActive (default true)

### LoginRequest
Code, RedirectUri

### RegisterRequest
Email, Password, OrganizationName, RegistrationCode?, PlanId?

---

## Config Gotchas

- `KeyClock` section name (typo) in appsettings.json, bound to `KeyclockSettings` class
- `DatabaseProvider` must be `"SqlServer"` - MySql throws
- Default `ConnectionStrings:SqlServerConnection` uses `Trusted_Connection` (machine-specific)
- No migrations committed yet (`Migrations/` is empty)
