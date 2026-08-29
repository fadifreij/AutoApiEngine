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
    public class ApiKeyRepository : GenericRepository<ApiKey>, IApiKeyRepository
    {
        private readonly ApplicationDbContext _context;

        public ApiKeyRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
        {
            _context = applicationDbContext;
        }

        public async Task<IEnumerable<ApiKey>> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken)
        {
            return await _context.ApiKeys
                .Where(k => k.OrganizationId == organizationId)
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> ExistsByNameAndOrganizationAsync(string name, Guid organizationId, Guid? excludeId = null, CancellationToken cancellationToken = default)
        {
            return await _context.ApiKeys
                .AnyAsync(k =>
                    k.Name == name &&
                    k.OrganizationId == organizationId &&
                    (excludeId == null || k.Id != excludeId.Value), cancellationToken);
        }

        public async Task<ApiKey> GetByIdWithOrganizationAsync(string id, CancellationToken cancellationToken = default)
        {
            if (!Guid.TryParse(id, out var guidId))
                throw new KeyNotFoundException($"ApiKey with id {id} not found.");

            var entity = await _context.ApiKeys
                .Include(k => k.Organization)
                .FirstOrDefaultAsync(k => k.Id == guidId, cancellationToken);

            if (entity == null)
                throw new KeyNotFoundException($"ApiKey with id {id} not found.");

            return entity;
        }
    }
}