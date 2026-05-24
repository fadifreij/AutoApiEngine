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
    public class WorkspaceRepository : GenericRepository<Workspace>, IWorkspaceRepository
    {
        private readonly ApplicationDbContext _context;

        public WorkspaceRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
        {
            _context = applicationDbContext;
        }

        public async Task<IEnumerable<Workspace>> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken)
        {
            return await _context.Workspaces
                .Where(w => w.OrganizationId == organizationId)
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> ExistsByNameAndOrganizationAsync(string name, Guid organizationId, Guid? excludeId = null, CancellationToken cancellationToken = default)
        {
            return await _context.Workspaces
                .AnyAsync(w =>
                    w.Name == name &&
                    w.OrganizationId == organizationId &&
                    (excludeId == null || w.Id != excludeId.Value), cancellationToken);
        }

        public async Task<Workspace> GetByIdWithOrganizationAsync(string id, CancellationToken cancellationToken = default)
        {
            if (!Guid.TryParse(id, out var guidId))
                throw new KeyNotFoundException($"Workspace with id {id} not found.");

            var entity = await _context.Workspaces
                .Include(w => w.Organization)
                .FirstOrDefaultAsync(w => w.Id == guidId, cancellationToken);

            if (entity == null)
                throw new KeyNotFoundException($"Workspace with id {id} not found.");

            return entity;
        }
    }
}