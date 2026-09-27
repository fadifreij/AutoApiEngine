using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Persistence.Context;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.Services.Repositories.Common;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.Services.Repositories
{
    public class ApiKeyPermissionRepository : GenericRepository<ApiKeyPermission>, IApiKeyPermissionRepository
    {
        private readonly ApplicationDbContext _context;

        public ApiKeyPermissionRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
        {
            _context = applicationDbContext;
        }

        /// <summary>
        /// The entire in-scope predicate in ONE SQL query, including both IS NULL wildcard branches.
        /// Do not "optimise" this into fetch-then-filter-in-C#: that shape cannot express the wildcard
        /// branches, and a row belonging to a foreign database would then drive control flow (a SalesDb
        /// wildcard making an OrdersDb request key-required).
        /// <para>
        /// <c>workspaceId</c> is load-bearing and must stay in the predicate: org separation rests on it
        /// being a globally unique Guid, since ApiKeyPermission has no OrganizationId of its own.
        /// </para>
        /// <para>
        /// No LOWER() on either side of the ObjectName comparison — the table collation is
        /// utf8mb4_0900_ai_ci, i.e. already case- AND accent-insensitive. Adding LOWER() would only
        /// defeat the index.
        /// </para>
        /// <para>
        /// Rows from ALL keys are returned on purpose: the filter resolves the presented key first and
        /// then selects this key's rows out of the set for the grant union.
        /// </para>
        /// </summary>
        public async Task<IEnumerable<ApiKeyPermission>> GetInScopeAsync(string objectName, string verb, Guid workspaceId, string databaseName, CancellationToken cancellationToken = default)
        {
            return await _context.ApiKeyPermissions
                .AsNoTracking()
                .Where(r =>
                    (r.ObjectName == null || r.ObjectName == objectName) &&
                    (r.Verb == null || r.Verb == verb) &&
                    r.WorkspaceId == workspaceId &&
                    r.DatabaseName == databaseName)
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> ExistsDenyAsync(Guid apiKeyId, string objectName, string? verb, Guid workspaceId, string databaseName, CancellationToken cancellationToken = default)
        {
            // verb supplied -> L1 (this object + this verb); verb null -> L2 (this object, any verb).
            return await _context.ApiKeyPermissions
                .AsNoTracking()
                .AnyAsync(p =>
                    p.ApiKeyId == apiKeyId &&
                    p.IsDeny &&
                    p.ObjectName == objectName &&
                    (verb == null || p.Verb == verb) &&
                    p.WorkspaceId == workspaceId &&
                    p.DatabaseName == databaseName, cancellationToken);
        }

        /// <summary>
        /// Plan §5.3 redundancy check: existing row A already covers the incoming scope B when
        /// (A.ObjectName IS NULL OR A.ObjectName = B.ObjectName) AND (A.Verb IS NULL OR A.Verb = B.Verb).
        /// <para>
        /// Note this is intentionally NOT symmetric: when the incoming scope is itself a wildcard
        /// (B.ObjectName == null), only another wildcard row can cover it — a narrower row like
        /// (Orders, GET) does not cover (NULL, GET). That falls out of the formula, so do not
        /// "simplify" it by short-circuiting on a null incoming value.
        /// </para>
        /// <para>
        /// <paramref name="isDeny"/> is part of the test: a deny row is never "covered" by a grant row,
        /// so the grant and deny checks stay separate. The unique index excludes IsDeny on purpose.
        /// </para>
        /// </summary>
        public async Task<bool> ExistsBroaderScopeAsync(Guid apiKeyId, string? objectName, string? verb, Guid workspaceId, string databaseName, bool isDeny, CancellationToken cancellationToken = default)
        {
            return await _context.ApiKeyPermissions
                .AsNoTracking()
                .AnyAsync(p =>
                    p.ApiKeyId == apiKeyId &&
                    p.IsDeny == isDeny &&
                    (p.ObjectName == null || p.ObjectName == objectName) &&
                    (p.Verb == null || p.Verb == verb) &&
                    p.WorkspaceId == workspaceId &&
                    p.DatabaseName == databaseName, cancellationToken);
        }

        /// <summary>
        /// Exact 5-tuple duplicate check, the backstop for the unique index on providers that cannot
        /// enforce it for NULL-bearing rows.
        /// <para>
        /// WHY THIS IS NEEDED: SQL Server treats NULL = NULL inside a UNIQUE index, so a second
        /// (key, NULL, NULL, ws, db) row is rejected by the database. MySQL treats NULLs as DISTINCT,
        /// so the index happily accepts that duplicate — verified live against the dev container.
        /// This method compares NULLs explicitly so both providers behave the same. The unique index
        /// stays: it is still a valid backstop for the non-NULL cases and for direct DB writes.
        /// </para>
        /// <para>
        /// IsDeny is included because the unique index deliberately excludes it — grant and deny at the
        /// same 5-tuple cannot coexist in the database, so a duplicate of one is not a duplicate of the other.
        /// </para>
        /// </summary>
        public async Task<bool> ExistsExactScopeAsync(Guid apiKeyId, string? objectName, string? verb, Guid workspaceId, string databaseName, bool isDeny, CancellationToken cancellationToken = default)
        {
            return await _context.ApiKeyPermissions
                .AsNoTracking()
                .AnyAsync(p =>
                    p.ApiKeyId == apiKeyId &&
                    ((p.ObjectName == null && objectName == null) || p.ObjectName == objectName) &&
                    ((p.Verb == null && verb == null) || p.Verb == verb) &&
                    p.WorkspaceId == workspaceId &&
                    p.DatabaseName == databaseName &&
                    p.IsDeny == isDeny, cancellationToken);
        }

        /// <summary>
        /// Grant rows on this exact object that name a specific verb. Backs the "deny all verbs over an
        /// existing explicit grant" check (plan §5.3): the rows are returned rather than a bool so the
        /// controller can name the offending verb(s) in the 400 message the FE shows verbatim.
        /// <para>
        /// Note this is NOT expressible via <see cref="ExistsBroaderScopeAsync"/>: with an incoming
        /// <c>verb = null</c> that method's <c>(p.Verb == null || p.Verb == verb)</c> branch collapses to
        /// <c>p.Verb == null</c>, so it matches only wildcard-verb grants — not the explicit-verb grants
        /// this check is looking for.
        /// </para>
        /// </summary>
        public async Task<IEnumerable<ApiKeyPermission>> GetExplicitVerbGrantsAsync(Guid apiKeyId, string objectName, Guid workspaceId, string databaseName, CancellationToken cancellationToken = default)
        {
            return await _context.ApiKeyPermissions
                .AsNoTracking()
                .Where(p =>
                    p.ApiKeyId == apiKeyId &&
                    !p.IsDeny &&
                    p.ObjectName == objectName &&
                    p.Verb != null &&
                    p.WorkspaceId == workspaceId &&
                    p.DatabaseName == databaseName)
                .ToListAsync(cancellationToken);
        }

        public async Task<IEnumerable<ApiKeyPermission>> GetByApiKeyIdAsync(Guid apiKeyId, CancellationToken cancellationToken = default)        {
            return await _context.ApiKeyPermissions
                .AsNoTracking()
                .Include(p => p.ApiKey)
                .Where(p => p.ApiKeyId == apiKeyId)
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// Scoped by <c>ApiKey.OrganizationId</c> OR the owning workspace's <c>OrganizationId</c>. The
        /// second branch is the recovery path: a cross-org row (a key from org A pointing at org B's
        /// workspace) is at least visible to the org that owns the workspace, instead of being invisible
        /// to both orgs. It is a visibility/recovery measure, NOT an authorization mechanism.
        /// <para>
        /// ApiKeyPermission deliberately has NO Workspace navigation property, so the workspace-org
        /// branch is a correlated EXISTS against the Workspaces DbSet rather than a navigation join.
        /// ApiKey IS included so the DTO projection can read ApiKeyName.
        /// </para>
        /// </summary>
        public async Task<IEnumerable<ApiKeyPermission>> GetByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
        {
            return await _context.ApiKeyPermissions
                .AsNoTracking()
                .Include(p => p.ApiKey)
                .Where(p =>
                    p.ApiKey.OrganizationId == organizationId ||
                    _context.Workspaces.Any(w => w.Id == p.WorkspaceId && w.OrganizationId == organizationId))
                .ToListAsync(cancellationToken);
        }
    }
}
