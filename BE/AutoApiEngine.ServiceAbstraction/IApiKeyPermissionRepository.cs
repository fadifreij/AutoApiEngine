using AutoApiEngine.Domain.Entities;
using AutoApiEngine.ServiceAbstraction.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.ServiceAbstraction
{
    public interface IApiKeyPermissionRepository : IGenericRepository<ApiKeyPermission>
    {
        /// <summary>
        /// Every permission row whose scope covers (objectName, verb, workspaceId, databaseName),
        /// wildcard rows included. The whole predicate is one SQL query.
        /// Returns rows from ANY key on purpose: the caller resolves the presented key first, then
        /// filters this set by <c>ApiKeyId</c> for the grant union.
        /// </summary>
        Task<IEnumerable<ApiKeyPermission>> GetInScopeAsync(string objectName, string verb, Guid workspaceId, string databaseName, CancellationToken cancellationToken);

        /// <summary>L1 when <paramref name="verb"/> is supplied, L2 (all verbs on the object) when null.</summary>
        Task<bool> ExistsDenyAsync(Guid apiKeyId, string objectName, string? verb, Guid workspaceId, string databaseName, CancellationToken cancellationToken);

        /// <summary>
        /// True when an existing row already covers the incoming scope, i.e. the plan §5.3 redundancy
        /// check. A deny row is never "covered" by a grant row, hence the explicit <paramref name="isDeny"/>.
        /// </summary>
        Task<bool> ExistsBroaderScopeAsync(Guid apiKeyId, string? objectName, string? verb, Guid workspaceId, string databaseName, bool isDeny, CancellationToken cancellationToken);

        /// <summary>Exact 5-tuple duplicate check. Provider-agnostic — see the implementation note.</summary>
        Task<bool> ExistsExactScopeAsync(Guid apiKeyId, string? objectName, string? verb, Guid workspaceId, string databaseName, bool isDeny, CancellationToken cancellationToken);

        /// <summary>
        /// Grant rows on this exact object that name a specific verb (<c>Verb != null</c>). Used by the
        /// controller to reject an L2 deny (<c>verb = null</c>, "deny all verbs") that would silently
        /// override a narrower grant, and to name the offending verb(s) in the 400 message.
        /// </summary>
        Task<IEnumerable<ApiKeyPermission>> GetExplicitVerbGrantsAsync(Guid apiKeyId, string objectName, Guid workspaceId, string databaseName, CancellationToken cancellationToken);

        Task<IEnumerable<ApiKeyPermission>> GetByApiKeyIdAsync(Guid apiKeyId, CancellationToken cancellationToken);

        /// <summary>Scoped by ApiKey.OrganizationId OR the owning workspace's OrganizationId (recovery path).</summary>
        Task<IEnumerable<ApiKeyPermission>> GetByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);
    }
}
