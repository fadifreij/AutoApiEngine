# AutoApiEngine Backend - Development Guide

## Architecture Overview

Clean Architecture with 6 projects targeting **.NET 10.0**:

```
AutoApiEngine.ApiServices        -> Entry point (Web Host, DI, Middleware, Config)
AutoApiEngine.Domain             -> Entities, Enums, BaseEntity (zero dependencies)
AutoApiEngine.Persistence        -> EF Core DbContext, Migrations
AutoApiEngine.ServiceAbstraction -> Interfaces, DTOs, Contracts
AutoApiEngine.Services           -> Service and Repository implementations
AutoApiEngine.Presentation       -> Controllers, Hubs, BaseController
```

## Project Dependencies

- Domain has zero dependencies
- Persistence depends on Domain
- ServiceAbstraction depends on Domain
- Services depends on Persistence and ServiceAbstraction
- Presentation depends on Domain, Persistence, ServiceAbstraction, Services
- ApiServices depends on Persistence, Presentation, Services

## Layer Conventions

### Domain (AutoApiEngine.Domain)
- Entities in Entities/ folder extend BaseEntity (Id: Guid, CreatedAt: DateTime)
- Enums in Enums/ folder
- Data annotations: [Column(TypeName = "VARCHAR")], [StringLength(n)], [Required]
- String defaults: = string.Empty, nullable with ?
- Navigation properties: = null! for required, collections for optional
- Zero external dependencies

### Persistence (AutoApiEngine.Persistence)
- ApplicationDbContext in Context/ folder
- DbSet<T> for each aggregate root
- OnModelCreating: relationships, precision, seed data
- Migrations stored in ApiServices/Migrations/

### ServiceAbstraction (AutoApiEngine.ServiceAbstraction)
- Interfaces at root level: I{Entity}Repository, IService
- Generic repo interface: Common/IGenricRepository<T> (note: typo Genric preserved)
- DTOs in DTO/ folder - simple POCOs, = string.Empty defaults, ? for optional
- Extends Domain only

### Services (AutoApiEngine.Services)
- Repositories in Repositories/ - extend GenericRepository<Entity>, implement interface
- Services in feature folders (AuthServices/, DatabaseManagementServices/)
- Constructor injection of ApplicationDbContext or other services
- Extends Persistence and ServiceAbstraction

### Presentation (AutoApiEngine.Presentation)
- Controllers in Controllers/ - [ApiController] + [Route("api/[controller]")] or [Route("api/name")]
- BaseController: wraps actions in HandleRequestAsync<T>(Func<Task<T>>) - catches KeyNotFoundException->404, ArgumentException->400, Exception->500
- Hubs in HubServices/
- Two controller patterns:
  1. Extends BaseController + constructor injection + HandleRequestAsync (CRUD style)
  2. Extends ControllerBase + [FromServices] per action (Auth style)

### ApiServices (AutoApiEngine.ApiServices)
- Program.cs - minimal hosting, no Startup.cs
- appsettings.json and appsettings.Development.json
- Providers/ServiceCollectionExtensions.cs - DI registration extension method
- Providers/DataBaseProvider.cs - database context configuration
- http/ - .http test files
- Migrations/ - EF Core migrations

## DI Pattern (Program.cs)
- Controllers with JSON config: CamelCase naming, string enum converters, indented output
- CORS policy AllowFrontend for http://localhost:4200 and https://localhost:7002
- builder.Services.AddApiServices(builder.Configuration) from Providers/
- builder.AddDatabaseContext(databaseProvider) from Providers/

## CRUD Pattern

### Controller extends BaseController with constructor injection
- All actions wrapped in HandleRequestAsync(async () => { ... })
- CancellationToken parameter: CancellationToken cancellationToken = default
- [FromBody] for POST/PUT DTOs
- Route: [Route("api/[controller]")]

### Repository Interface extends IGenericRepository<Entity>
### Repository Implementation extends GenericRepository<Entity>(ApplicationDbContext)

### DTOs in ServiceAbstraction/DTO/
- Create{Entity}Dto, Update{Entity}Dto
- = string.Empty defaults, ? for optional fields

## IGenericRepository Methods
- GetAllAsync(CancellationToken ct)
- GetByIdAsync(string id, CancellationToken ct) - throws KeyNotFoundException if not found
- FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct)
- AddAsync(T entity, CancellationToken ct)
- UpdateAsync(T entity, CancellationToken ct)
- DeleteAsync(string id, CancellationToken ct)

## Key Config Values
- API: http://localhost:5145, https://localhost:7002
- Keycloak: http://localhost:8081, Realm: ApiEngineRealm, ClientId: api-engine-app
- DatabaseProvider: SqlServer
- CORS Origins: http://localhost:4200, https://localhost:7002

## Auth Flow
1. Frontend redirects to Keycloak for OAuth2 Authorization Code flow
2. Keycloak callback -> frontend sends code to POST /api/auth/Login
3. Backend exchanges code for tokens, sets refresh_token as HTTP-only cookie
4. Token refresh via POST /api/auth/refresh-token (cookie-based)
5. Logout via POST /api/auth/logout (clears cookie, returns Keycloak end-session URL)
6. JWT Bearer authentication configured with Keycloak authority

## EF Core Commands (run from AutoApiEngine.ApiServices)
dotnet ef migrations add MigrationName --startup-project . --project ../AutoApiEngine.Persistence
dotnet ef database update --startup-project . --project ../AutoApiEngine.Persistence

## Custom Repository Methods for Parent-Child Relationships
When querying by parent foreign key (e.g., OrganizationId -> Workspaces):
1. Add method to interface: Task<IEnumerable<Child>> GetBy{Parent}IdAsync(Guid parentId, CancellationToken ct)
2. Implement in repository: query _context.{Children}.Where(c => c.{Parent}Id == parentId).ToListAsync(ct)
3. Controller calls the repository method directly (not FindAsync)
4. Route pattern: [HttpGet("{parent}/{parentId}")] placed before [HttpGet("{id}")]

## Route Ordering
- Literal route segments MUST be placed before parameter segments (e.g., [HttpGet("organization/{id}")] before [HttpGet("{id}")])
- Otherwise the parameter segment will swallow the literal match

## Naming Conventions
- Controllers: {Feature}Controller.cs
- Entities: PascalCase singular (Workspace.cs)
- DTOs: {Action}{Entity}Dto.cs (CreateWorkspaceDto.cs)
- Repositories: {Entity}Repository.cs, I{Entity}Repository.cs
- Services: {Feature}Service.cs, I{Feature}Service.cs
- Folders: PascalCase plural (Controllers/, Entities/, Repositories/)

## Important Notes
- Typo preserved: IGenricRepository (not Generic)
- Typo preserved: KeyclockSettings (not Keycloak)
- Id is Guid type but GetByIdAsync takes string id parameter
- All entities use Guid keys (generated by EF)
- Soft delete pattern: IsActive boolean flag on entities
- BaseController error responses use { message: string } format
- AuthController error responses use { success: false, error: string } format
- Guid.TryParse required before calling FindAsync (Id is Guid but route params are string)
