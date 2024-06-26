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
        _dbContext.Set<T>().AsNoTracking();
        return await _dbContext.Set<T>()
            .FindAsync(id);
    }

    public virtual async Task<IReadOnlyList<T>> GetAllAsync()
    {
        _dbContext.Set<T>().AsNoTracking();
        return await _dbContext.Set<T>().ToListAsync();
    }

    public virtual async Task UpdateAsync(T entity)
    {
        _dbContext.Entry(entity).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();
    }
}