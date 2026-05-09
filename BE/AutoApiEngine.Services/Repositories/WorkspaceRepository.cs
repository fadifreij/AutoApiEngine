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
    }
}