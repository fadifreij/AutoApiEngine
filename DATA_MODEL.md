# Data Model

## Entity Relationships

```
Organization (1) ----< (N) Workspace
Organization (1) ----< (N) Subscription
Subscription (N) >---- (1) Plan
```

## BaseEntity (Common Base)

```
Guid Id          (PK)
DateTime CreatedAt
```

---

## Organization

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| CreatedAt | DateTime | |
| Name | string(250) | Required |
| Description | string(250) | |
| IsActive | bool | default true |
| RegistrationCode | string(100) | Required |
| TrialEndsAt | DateTime? | |
| SubscriptionEndsAt | DateTime? | |
| IsTrial | bool | Computed: TrialEndsAt.HasValue && UtcNow <= TrialEndsAt |
| IsSubscriptionActive | bool | Computed: SubscriptionEndsAt.HasValue && UtcNow <= SubscriptionEndsAt |
| IsExpired | bool | Computed: !IsTrial && !IsSubscriptionActive |
| CurrentSubscriptionId | Guid? | FK -> Subscription |
| CurrentSubscription | Subscription? | Navigation |
| Workspaces | ICollection<Workspace> | Navigation |

---

## Workspace

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| CreatedAt | DateTime | |
| Name | string(250) | Required |
| EncryptionKey | string(75)? | **Stored but never consumed** |
| DbUserName | string(50)? | For external DB connections |
| DbPassword | string(50)? | For external DB connections |
| DatabaseName | string(50)? | Database name |
| DatabaseEngine | enum | SqlServer, PostgreSql, MySql, Sqlite |
| IsActive | bool | default true |
| OrganizationId | Guid | FK -> Organization |

**Current state:** EncryptionKey field exists end-to-end but no actual encryption/decryption logic is implemented.

---

## Plan

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| CreatedAt | DateTime | |
| Name | string(100) | e.g. Free, Basic, Pro, Enterprise |
| Price | decimal(18,2) | |
| DurationInDays | int | e.g. 30, 365 |
| MaxWorkspaces | int | |
| MaxUsers | int | |
| IsActive | bool | default true |

---

## Subscription

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| CreatedAt | DateTime | |
| OrganizationId | Guid | FK -> Organization |
| PlanId | Guid | FK -> Plan |
| StartDate | DateTime | |
| EndDate | DateTime | |
| AmountPaid | decimal(18,2) | |
| Status | enum(PaymentStatus) | Pending, Completed, Failed |
| IsActive | bool | Computed: UtcNow >= StartDate && UtcNow <= EndDate |

---

## Enums

### DatabaseEngine
- `SqlServer` (default)
- `PostgreSql`
- `MySql`
- `Sqlite`

### PaymentStatus
- `Pending`
- `Completed`
- `Failed`

---

## Current State Notes

- **No EF migrations exist yet** - `Migrations/` directory is empty
- **No actual encryption** - EncryptionKey field is stored as plain text, never consumed
- **Backend DB provider** is always SqlServer (MySql throws in `DataBaseProvider.cs`)
- **Per-workspace DatabaseEngine** is stored but not yet used for dynamic connection routing
