using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Persistence.Context;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.Services.Repositories.Common;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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

        /// <summary>
        /// Batch-loads workspaces by id.
        /// <para>
        /// ⚠️ This deliberately does NOT use <c>ids.Contains(w.Id)</c>. Guid is stored as
        /// <c>char(36)</c>, and the MySQL provider cannot resolve a type mapping for a
        /// <see cref="Guid"/> <b>collection</b> parameter — it throws
        /// <c>"Expression '@ids' in the SQL tree does not have a type mapping assigned"</c> at runtime.
        /// Scalar <c>w.Id == guid</c> equality translates fine (see GetByIdWithOrganizationAsync), so the
        /// predicate is built as an OR-chain of scalar equalities instead. It stays a single server-side
        /// round-trip (no N+1, no client evaluation) and works on both providers.
        /// <para>
        /// Do not "simplify" this back to Contains() — it compiled fine and failed only at runtime.
        /// </para>
        /// </summary>
        public async Task<IEnumerable<Workspace>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            var idList = ids.Distinct().ToList();
            if (idList.Count == 0)
                return new List<Workspace>();

            // w => w.Id == id1 || w.Id == id2 || ...
            var parameter = Expression.Parameter(typeof(Workspace), "w");
            var idProperty = Expression.Property(parameter, nameof(Workspace.Id));
            Expression body = Expression.Constant(false);
            foreach (var id in idList)
                body = Expression.OrElse(body, Expression.Equal(idProperty, Expression.Constant(id)));

            var predicate = Expression.Lambda<Func<Workspace, bool>>(body, parameter);

            return await _context.Workspaces
                .AsNoTracking()
                .Where(predicate)
                .ToListAsync(cancellationToken);
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