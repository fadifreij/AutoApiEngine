using AutoApiEngine.Domain.Common;
using AutoApiEngine.Persistence.Context;
using AutoApiEngine.ServiceAbstraction.Common;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace AutoApiEngine.Services.Repositories.Common
{
    public class GenericRepository<T>: IGenericRepository<T> where T : BaseEntity
    {
        private readonly ApplicationDbContext _context;
        private readonly DbSet<T> _dbSet;

        public GenericRepository(ApplicationDbContext context)
        {
            _context = context;
            _dbSet = context.Set<T>();
        }

        public async Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _dbSet.ToListAsync(cancellationToken);
        }

        public async Task<T> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            if (!Guid.TryParse(id, out var guidId))
                throw new KeyNotFoundException($"Entity with id {id} not found.");

            var entity = await _dbSet.FindAsync(new object[] { guidId }, cancellationToken);
            if (entity == null)
                throw new KeyNotFoundException($"Entity with id {id} not found.");

            return entity;
        }

        public async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await _dbSet.Where(predicate).ToListAsync(cancellationToken);
        }

        public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            await _dbSet.AddAsync(entity, cancellationToken);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
        {
            _dbSet.Update(entity);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            if (!Guid.TryParse(id, out var guidId))
                throw new KeyNotFoundException($"Entity with id {id} not found.");

            var entity = await _dbSet.FindAsync(new object[] { guidId });
            if (entity != null)
            {
                _dbSet.Remove(entity);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
