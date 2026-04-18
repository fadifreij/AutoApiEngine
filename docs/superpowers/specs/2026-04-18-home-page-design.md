# Home Page Design — `/home`

## Overview

A new main landing page that appears immediately after login, replacing the current root redirect to `/workspaces`. The page presents three core platform sections: **Workspaces**, **API Management**, and **Security**.

## Routes

| Route | Component | Purpose |
|---|---|---|
| `/home` | `HomeComponent` | New post-login landing page |
| `/workspaces` | `MainWorkspaceComponent` | Existing workspace list/management |
| `/workspaces/dashboard` | `DashboardComponent` | Existing workspace detail dashboard |
| `/api-management` | TBD | New API management section |
| `/security` | TBD | New security section |

## Routing Changes

1. Root `/` redirect updated: `/` → `/home`
2. `url_after_login` constant updated: `'/workspaces'` → `'/home'`
3. Shell topbar nav updated with links: Home, Workspaces Management, API Management, Security

## Page Structure

### Layout
- Full-height page, scrollable content
- Page max-width: 1200px, centered, with vertical padding
- Three sections stacked vertically
- Each section: header (icon + title + description) + content + action

### Section: Workspaces
- **Icon**: Database SVG (inline)
- **Title**: "Workspaces"
- **Description**: "Your database workspaces — create, manage, backup, and restore"
- **Summary strip**: "X workspaces · Y active" stats bar
- **Content**: Grid of workspace cards (2-3 columns, responsive)
  - Each card: database SVG illustration, workspace name, status badge, last backup date
  - "Manage" button per card navigates to `/workspaces/dashboard`
- **CTA**: "Create New Workspace →" button → `/workspaces/create`

### Section: API Management
- **Icon**: Lightning bolt / API symbol SVG (inline)
- **Title**: "API Management"
- **Description**: "Generate REST APIs for each table in your database workspaces"
- **Summary strip**: "X total APIs · Y endpoints · Z requests this month" stats bar
- **Content**: List of workspaces with table count and API status
  - Clicking a workspace expands inline table list OR navigates to `/api-management` for that workspace
- **CTA**: "Manage APIs →" button → `/api-management`

### Section: Security
- **Icon**: Shield/lock SVG (inline)
- **Title**: "Security"
- **Description**: "Protect your APIs with keys, rate limits, IP rules, and authentication"
- **Summary strip**: security feature status overview
- **Content**: Grid of 5 feature cards
  1. **API Keys** — per-workspace API key management, regenerate, revoke
  2. **Rate Limiting** — requests per minute/hour limits per endpoint
  3. **IP Allowlist** — whitelist/blocklist IP addresses
  4. **Authentication** — JWT, API keys, OAuth method toggles
  5. **Audit Log** — request logging with filterable timeline
- Each card: feature name, status badge (enabled/disabled), "Configure →" button
- **CTA**: "Security Dashboard →" button → `/security`

## Design System

- Uses `--aae-*` CSS variables (all theme-aware)
- Typography: DM Sans font (already loaded in `styles.scss`)
- Spacing: 8px base unit
- Border radius: 12px for cards, 10px for buttons, 8px for inputs
- Animations: fade-in + translateY on page load, hover transitions on cards

## Placeholder Routes

`/api-management` and `/security` will be implemented as future phases. This spec covers only the `/home` page and routing changes.

## Shell Navigation Updates

Update `ShellComponent` template:
- Replace hardcoded nav links with dynamic nav items
- Add `/home` link (Dashboard/Home icon)
- Add `/api-management` link (API icon)
- Add `/security` link (Shield icon)

## Success Criteria

1. After login, user lands on `/home` with three clear sections
2. Each section has functional navigation to its respective area
3. Workspaces section shows existing workspace data from `WorkspaceStore`
4. Page is fully theme-compatible (dark/light/ocean/forest/midnight)
5. Responsive layout on mobile (single column, stacked sections)
6. No PrimeNG component dependencies for section layout — custom HTML/CSS only