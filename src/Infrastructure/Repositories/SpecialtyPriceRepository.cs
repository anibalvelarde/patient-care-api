using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Repositories;

/// <summary>
/// WP-39: specialty price-sheet data access. Both reads use a single Include (one SQL join) —
/// never a per-specialty query (WP-29 house rule).
/// </summary>
public class SpecialtyPriceRepository : ISpecialtyPriceRepository
{
    private readonly ApplicationDbContext _dbContext;

    public SpecialtyPriceRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<SpecialtyType>> GetAllWithPricesAsync()
    {
        return await _dbContext.SpecialtyTypes
            .AsNoTracking()
            .Include(s => s.DurationPrices)
            .ToListAsync();
    }

    public async Task<SpecialtyType?> GetWithPricesAsync(int specialtyTypeId)
    {
        return await _dbContext.SpecialtyTypes
            .AsNoTracking()
            .Include(s => s.DurationPrices)
            .FirstOrDefaultAsync(s => s.Id == specialtyTypeId);
    }

    public async Task AddRangeAsync(IReadOnlyCollection<SpecialtyDurationPrice> rows)
    {
        await _dbContext.SpecialtyDurationPrices.AddRangeAsync(rows);
        await _dbContext.SaveChangesAsync();
    }
}
