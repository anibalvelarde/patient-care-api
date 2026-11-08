using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class EfRepository<T> : IRepository<T> where T : class
{
    protected readonly ApplicationDbContext _dbContext;

    public EfRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public virtual async Task<T> AddAsync(T entity)
    {
        await _dbContext.Set<T>().AddAsync(entity);
        await _dbContext.SaveChangesAsync();
        return entity;
    }

    public async Task DeleteAsync(T entity)
    {
        _dbContext.Set<T>().Remove(entity);
        await _dbContext.SaveChangesAsync();
    }

    public virtual async Task<T?> GetByIdAsync(int id)
    {
        return await _dbContext.Set<T>()
            .FindAsync(id);
    }

    public virtual async Task<IReadOnlyList<T>> GetAllAsync()
    {
        return await _dbContext.Set<T>()
            .AsNoTracking()
            .ToListAsync();
    }

    public virtual async Task UpdateAsync(T entity)
    {
        // WP-60 (F-15b): a TRACKED entity already knows what changed — forcing State=Modified flagged
        // every column, so the change log listed 20+ "changed" fields for a 4-field late-fee write.
        // Only a detached entity needs to be attached as Modified (same guard as AuthRepository).
        if (_dbContext.Entry(entity).State == EntityState.Detached)
        {
            _dbContext.Entry(entity).State = EntityState.Modified;
        }
        await _dbContext.SaveChangesAsync();
    }
}