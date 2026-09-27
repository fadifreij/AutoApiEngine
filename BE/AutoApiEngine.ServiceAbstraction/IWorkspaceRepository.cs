using AutoApiEngine.Domain.Entities;
using AutoApiEngine.ServiceAbstraction.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.ServiceAbstraction
{
    public interface IWorkspaceRepository : IGenericRepository<Workspace>   
    {
        Task<IEnumerable<Workspace>> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken);
        /// <summary>Batch-load workspaces by id. See the implementation for why this is not <c>Contains()</c>.</summary>
        Task<IEnumerable<Workspace>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
        Task<bool> ExistsByNameAndOrganizationAsync(string name, Guid organizationId, Guid? excludeId = null, CancellationToken cancellationToken = default);
        Task<Workspace> GetByIdWithOrganizationAsync(string id, CancellationToken cancellationToken = default);
    }
}