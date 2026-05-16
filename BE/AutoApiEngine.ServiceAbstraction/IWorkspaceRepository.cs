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
    Task<bool> ExistsByNameAndOrganizationAsync(string name, Guid organizationId, Guid? excludeId = null, CancellationToken cancellationToken = default);
    }
}