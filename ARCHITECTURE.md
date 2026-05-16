# AutoApiEngine Architecture

## Overview

AutoApiEngine is a full-stack application that auto-generates CRUD APIs from database schemas. Users create "workspaces" backed by databases (hosted by us or external), and the system generates REST endpoints.

- **FE/**: Angular 21 standalone components, SSR with prerendering
- **BE/**: .NET 10 layered architecture (6 projects), EF Core, Keycloak auth
- **DockerImages/**: Dev infrastructure (Keycloak, SQL Server, MySQL)

---

## Layer Diagram

```
Browser (Angular SPA)
    |
    | HTTPS
    v
AutoApiEngine.ApiServices  (entry point, Program.cs)
    |
    ├── AutoApiEngine.Presentation  (Controllers, Hubs, BaseController)
    ├── AutoApiEngine.Services      (Repositories, Auth, DB Management)
    ├── AutoApiEngine.ServiceAbstraction  (Interfaces, DTOs)
    ├── AutoApiEngine.Persistence   (EF Core DbContext, seeding)
    └── AutoApiEngine.Domain        (Entities, Enums, BaseEntity)
```

## Dependency Flow

**ApiServices** references: Persistence, Presentation, Services
**Presentation** references: Domain, Persistence, ServiceAbstraction, Services
**Services** references: Persistence, ServiceAbstraction
**Persistence** references: Domain
**ServiceAbstraction** references: Domain
**Domain** - leaf, no project refs

No `.sln` file - run `dotnet` against individual `.csproj` files.

---

## Authentication Flow

1. User visits `/login` -> browser redirects to Keycloak OIDC endpoint
2. Keycloak redirects back to `/auth/callback?code=...`
3. Frontend calls `POST /api/auth/login { code, redirectUri }`
4. Backend exchanges code with Keycloak, sets `refresh_token` cookie (Secure)
5. On page reload: `AuthService.initAuth()` calls `POST /api/auth/refresh-token` (cookie-based)
6. Access token stored in-memory (not localStorage)

---

## Key Technology Choices

| Layer | Technology | Version |
|-------|-----------|---------|
| Frontend | Angular | 21.2 |
| Frontend build | @angular/build (Vite-based) | 21.2.7 |
| CSS | SCSS | - |
| Testing (FE) | Vitest | 4.0.8 |
| Backend | .NET | 10.0 |
| ORM | Entity Framework Core | 10.0.5 |
| Auth | Keycloak (self-hosted Docker) | - |
| Database (app) | SQL Server (Docker) | - |
| Database (Keycloak) | MySQL (Docker) | - |
| Infra | Docker Compose | - |

---

## Port Map

| Service | Host Port | Container Port |
|---------|-----------|---------------|
| Frontend dev server | 4200 | - |
| Backend API (HTTPS) | 7002 | - |
| Backend API (HTTP) | 5145 | - |
| Keycloak | 8081 | 8080 |
| SQL Server | 1433 | 1433 |
| MySQL | 3307 | 3306 |

---

## SSR Strategy

- All routes use `RenderMode.Prerender` (static HTML generated at build time)
- Guards early-return `true` on server via `isPlatformBrowser(PLATFORM_ID)`
- `AuthService` guards localStorage access with platform checks
