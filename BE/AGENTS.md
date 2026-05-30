# AutoApiEngine Backend (BE/)

Run `dotnet` commands against individual `.csproj` files (no `.sln`). All projects target `net10.0`.

## Entry Point

```
dotnet run --project BE/AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj --launch-profile https
```

Launch profiles: http://localhost:5145, https://localhost:7002.

## Projects

ApiServices (host) -> Presentation (controllers) -> Services (logic) -> ServiceAbstraction (interfaces) -> Persistence (EF) -> Domain (entities)

## Config Gotchas

| Issue | Detail |
|-------|--------|
| KeyClock typo | Section name is `KeyClock`, bound to `KeyclockSettings` (do NOT fix) |
| SqlServer only | `DatabaseProvider=SqlServer`; `MySql` throws |
| Connection string | Checked-in uses `Trusted_Connection` — override for Docker SQL Server |

## Key Conventions

- Controllers inherit `BaseController` and use `HandleRequestAsync(...)`.
- Repository: `IGenericRepository<T>` (file misnamed `IGenricRepository.cs`).
- Route ordering: literal segments before `[HttpGet("{id}")]`.

## EF Core Migrations

Run from `BE/AutoApiEngine.ApiServices/`:
```
dotnet ef migrations add <Name> --startup-project . --project ../AutoApiEngine.Persistence
dotnet ef database update --startup-project . --project ../AutoApiEngine.Persistence
```

## Auth

| Method | Route | Behavior |
|--------|-------|----------|
| POST | /api/auth/login | OAuth2 code exchange, sets `refresh_token` cookie |
| POST | /api/auth/refresh-token | Reads `refresh_token` cookie |
| POST | /api/auth/logout | Clears auth cookies |

## Detailed Reference

See `.opencode/agents/backend.md` for the full sub-agent instructions.
