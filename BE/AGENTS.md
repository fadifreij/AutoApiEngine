# AutoApiEngine Backend (BE/)

There is no repo-wide `.sln`; run `dotnet` commands against the relevant `.csproj`.

## Entry Point / Run
- Web host: `AutoApiEngine.ApiServices` (`BE/AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj`, `net10.0`).
- All BE projects target `net10.0` (you need a .NET 10 SDK installed).
- Run (matches FE default env): `dotnet run --project BE/AutoApiEngine.ApiServices/AutoApiEngine.ApiServices.csproj --launch-profile https`.
- Launch profiles are in `BE/AutoApiEngine.ApiServices/Properties/launchSettings.json`:
  http: `http://localhost:5145`
  https: `https://localhost:7002` (also `http://localhost:5145`)

## Config / Gotchas
- Config lives in `BE/AutoApiEngine.ApiServices/appsettings.json`.
- Keycloak settings section is spelled `KeyClock` and is bound to `KeyclockSettings` (typo is in code).
- `DatabaseProvider` must be exactly `SqlServer`; `MySql` is present but throws in `Providers/DataBaseProvider.cs`.
- The checked-in `ConnectionStrings:SqlServerConnection` uses a machine-specific `Trusted_Connection`; override it if you’re using the Docker SQL Server (`localhost:1433`).

## EF Core (Migrations)
- Migrations are under `BE/AutoApiEngine.ApiServices/Migrations/` (migrations assembly is `AutoApiEngine.ApiServices`).
- Run from `BE/AutoApiEngine.ApiServices/`:
  `dotnet ef migrations add <Name> --startup-project . --project ../AutoApiEngine.Persistence`
  `dotnet ef database update --startup-project . --project ../AutoApiEngine.Persistence`

## API Conventions In Tree
- CRUD controllers typically inherit `AutoApiEngine.Presentation.BaseController` and wrap work in `HandleRequestAsync(...)`.
- Generic repository type is `IGenericRepository<T>`, but the file is misnamed `AutoApiEngine.ServiceAbstraction/Common/IGenricRepository.cs`.
- Attribute routing: put literal segments before `[HttpGet("{id}")]` (see `WorkspacesController` route ordering).

## Auth Endpoints (Current Behavior)
- `POST /api/auth/login` expects `{ code, redirectUri }` (OAuth2 code exchange) and sets a `refresh_token` cookie with `Secure=true`.
- `POST /api/auth/refresh-token` reads the refresh token from the `refresh_token` cookie (request body is `{}` in FE).
- `BE/AutoApiEngine.ApiServices/http/Authentication.http` is stale (it still shows email/password + body refresh token).
