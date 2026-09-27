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

### Dynamic CRUD endpoints take an API key, not a JWT

`//[Authorize]` on `DynamicApiController` is intentionally commented out (D11).
Those endpoints authenticate via the **`X-Api-Key`** header, enforced by
`ApiKeyAuthFilter`:

```
X-Api-Key: <64-char plaintext key>
```

No prefix, no `Bearer`. The plaintext is returned **once**, as `plainKey` in the
`POST /api/keys` response, and is not retrievable afterwards — only its SHA-256 hex
hash is stored (`ApiKeyHasher.Hash`). The **Scopes** tab decides per-endpoint whether
a key is required: no permission row in scope means the endpoint is open (D1/D13).

Metadata endpoints (`api/schema/**`) stay `[Authorize]` and need `Authorization: Bearer <jwt>`.

### Scripted API verification

Do not try to log in via `POST /api/auth/login` (code exchange) or a Keycloak password
grant — the live `api-engine-app` client is confidential with
`clientAuthenticatorType: "client-jwt"`, so even a correct `client_secret` fails with
`invalid_client: "Parameter client_assertion_type is missing"`. Sign the private-key
JWT from `appsettings.json → KeyClock:ClientJwtKey` instead. Full recipe and the
temporary-user procedure are in `.opencode/instructions.md` → Speed Rules §4.

Endpoint shapes that are easy to guess wrong:

| Call | Correct shape |
|------|---------------|
| Object metadata | `GET /api/schema/{workspaceId}/columns?table=<name>` (**`table`**, not `objectName`) |
| Create key | `POST /api/keys` -> `plainKey` (not `apiKey`) |
| Key permissions | `GET|POST /api/keys/{apiKeyId}/permissions`, org-wide: `GET /api/permissions?organizationId=` |

## Detailed Reference

See `.opencode/agents/backend.md` for the full sub-agent instructions.
