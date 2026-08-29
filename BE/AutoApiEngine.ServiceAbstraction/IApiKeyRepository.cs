using AutoApiEngine.Domain.Entities;
using AutoApiEngine.ServiceAbstraction.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.ServiceAbstraction
{
    public interface IApiKeyRepository : IGenericRepository<ApiKey>
    {
        Task<IEnumerable<ApiKey>> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken);
        Task<bool> ExistsByNameAndOrganizationAsync(string name, Guid organizationId, Guid? excludeId = null, CancellationToken cancellationToken = default);
        Task<ApiKey> GetByIdWithOrganizationAsync(string id, CancellationToken cancellationToken = default);
    }
}