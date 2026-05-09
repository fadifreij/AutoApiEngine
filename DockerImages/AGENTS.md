# AutoApiEngine Docker Infrastructure - Development Guide

## Overview

Docker Compose orchestration for development environment with 3 active services:
- **MySQL 8.0** - Keycloak database backend
- **Keycloak 26.0.5** - Identity Provider (OAuth2/OIDC)
- **SQL Server 2022** - Application database for .NET backend

The .NET backend container is defined but commented out in docker-compose.yml (run locally during development).

## Directory Structure

```
DockerImages/
  docker-compose.yml          - Service orchestration
  read-me.txt                 - Basic setup notes
  back-end-app/
    Dockerfile                - Multi-stage .NET build (used when uncommented)
  KeyClock/
    Dockerfile                - Keycloak image with realm import
    build.txt                 - Build notes
    realm/
      realm-config.json       - ApiEngineRealm configuration
    old_realm/
      myrealm.json            - Legacy realm config (reference only)
    theme/
      apiengine/              - Custom Keycloak theme
        login/                - Login page templates
        email/                - Email templates
  mysql/
    Dockerfile                - MySQL with init script
    init.sql                  - Creates keycloak database and user
  sqlserver/
    Dockerfile                - SQL Server 2022 custom image
```

## Services

### MySQL (keycloak database)
- Image: mysql:8.0 (custom build from mysql/Dockerfile)
- Container name: mysql
- Port: 3307:3306 (host 3307 maps to container 3306)
- Environment: MYSQL_DATABASE=ApiEngineDB, MYSQL_USER=admin, MYSQL_PASSWORD=Welcome@123, MYSQL_ROOT_PASSWORD=root
- Health check: mysqladmin ping every 10s with 10 retries
- Volume: mysql_data:/var/lib/mysql (persistent named volume)
- Init: init.sql creates keycloak database and keycloak user

### Keycloak (Identity Provider)
- Image: Custom build from KeyClock/Dockerfile (base: quay.io/keycloak/keycloak:26.0.5)
- Container name: keycloak
- Port: 8081:8080 (host 8081 maps to container 8080)
- Command: start-dev --import-realm -Dkeycloak.theme.default=apiengine
- Environment: KEYCLOAK_ADMIN=admin, KEYCLOAK_ADMIN_PASSWORD=admin
- Database: KC_DB=mysql, KC_DB_URL=jdbc:mysql://mysql:3306/keycloak, KC_DB_USERNAME=keycloak, KC_DB_PASSWORD=keycloak
- SMTP: Gmail (fadi.freij38601@gmail.com) for email verification
- Volumes:
  - ./KeyClock/realm:/opt/keycloak/data/import (realm config)
  - ./KeyClock/theme:/opt/keycloak/themes (custom theme)
- Depends on: mysql (condition: service_healthy)

### SQL Server (Application database)
- Image: mcr.microsoft.com/mssql/server:2022-latest (custom build)
- Container name: sqlserver
- Port: 1433:1433
- Environment: ACCEPT_EULA=Y, SA_PASSWORD=Welcome@123
- Health check: sqlcmd -S localhost -U sa -P Welcome@123 -Q SELECT 1 every 10s
- Volume: sqlserver_data:/var/opt/mssql (persistent named volume)

### .NET App (Commented out - run locally)
- Multi-stage Dockerfile: .NET 8 SDK build -> ASP.NET 8 runtime
- Would expose port 5000:8080
- Would connect to sqlserver and keycloak via Docker network

## Keycloak Realm Configuration (ApiEngineRealm)

### Realm Settings
- Registration allowed: false (use app registration flow)
- Registration email as username: true
- Remember me: true
- Verify email: true
- Access token lifespan: 300 seconds (5 minutes)
- SSO session idle timeout: 1800 seconds (30 minutes)
- SSO session max lifespan: 36000 seconds (10 hours)
- Login theme: apiengine
- Email theme: apiengine
- Default signature algorithm: RS256

### Clients

#### api-engine-app (Frontend client)
- Type: Confidential (publicClient: false)
- Standard flow enabled: true (Authorization Code)
- Direct access grants: true
- Service accounts: enabled
- Root URL: http://localhost:4200
- Redirect URIs: http://localhost:4200/*, http://localhost:4200
- Web origins: http://localhost:4200
- Default scopes: web-origins, email, roles, profile, acr, organization, basic

#### api-engine-service (Backend service account)
- Type: Confidential with secret: api-engine-service-secret
- Service accounts: enabled
- Full scope allowed: true
- Has realm-management -> realm-admin role

### Custom Client Scope: organization
- Maps user attribute org to token claim organization
- Included in access tokens (not ID tokens)
- Requested in auth URL scope: openid organization

### Roles
- admin, user, realm-admin, default-roles-apienginerealm
- default-roles-apienginerealm is composite and includes user role

## Keycloak Theme (apiengine)

### Login Theme
- Parent: base
- Custom templates: login.ftl, register.ftl, error.ftl, template.ftl, login-reset-password.ftl, login-update-password.ftl, login-update-profile.ftl, login-verify-email.ftl
- Custom CSS: css/styles.css (360 lines, mirrors app design tokens)
- Custom JS: js/script.js (password toggle)
- Messages: messages/messages_en.properties (72 custom overrides)
- Branding: indigo/violet color scheme (--primary: #4f46e5) matching Angular app

### Email Theme
- Parent: base
- HTML templates: template.ftl, email-verification.ftl, password-reset.ftl, email-update-confirmation.ftl, executeActions.ftl, event-login_error.ftl
- Plain text versions in text/ directory
- Table-based HTML with indigo gradient header, branded footer

## Network and Communication

No custom Docker networks - all services use default bridge network.

### Inter-container communication
- Keycloak -> MySQL via hostname mysql:3306
- .NET app (local) -> SQL Server via localhost:1433
- .NET app (local) -> Keycloak via localhost:8081
- Frontend (local) -> Keycloak via localhost:8081
- Frontend (local) -> Backend via localhost:7002

### Port summary
| Service | Container Port | Host Port | Purpose |
|---------|---------------|-----------|---------|
| Keycloak | 8080 | 8081 | OIDC Identity Provider |
| MySQL | 3306 | 3307 | Keycloak database |
| SQL Server | 1433 | 1433 | Application database |

## Volume Mounts

### Named Volumes (persistent data)
- mysql_data -> /var/lib/mysql
- sqlserver_data -> /var/opt/mssql

### Bind Mounts (development/hot-reload)
- ./KeyClock/realm -> /opt/keycloak/data/import (realm JSON)
- ./KeyClock/theme -> /opt/keycloak/themes (live theme updates)

## Commands

### Start all services
docker-compose up -d

### Start with build
docker-compose up -d --build

### View logs
docker-compose logs -f keycloak
docker-compose logs -f mysql
docker-compose logs -f sqlserver

### Stop all services
docker-compose down

### Stop and remove volumes
docker-compose down -v

## Important Notes
- No .env files - all environment variables are inline in docker-compose.yml
- Keycloak realm is imported on every start (--import-realm flag)
- Keycloak theme is set as default via -Dkeycloak.theme.default=apiengine
- SMTP password is a Google App Password (fadi.freij38601@gmail.com)
- MySQL and SQL Server data persists across container restarts via named volumes
- The .NET backend is NOT containerized during development - run locally via dotnet run
- Frontend dev server runs locally via ng serve (not containerized)
