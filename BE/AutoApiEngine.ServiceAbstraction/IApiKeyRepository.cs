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

        /// <summary>
        /// Filter key lookup: matches only an ACTIVE, unexpired key, otherwise returns null.
        /// An expired or inactive key must be indistinguishable from an unknown one so the caller
        /// cannot tell the two apart from the response.
        /// </summary>
        Task<ApiKey?> FindActiveKeyByHashAsync(string keyHash, CancellationToken cancellationToken);
    }
}