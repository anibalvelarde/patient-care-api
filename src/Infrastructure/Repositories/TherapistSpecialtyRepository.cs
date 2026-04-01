using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class TherapistSpecialtyRepository : ITherapistSpecialtyRepository
{
    private readonly ApplicationDbContext _dbContext;

    public TherapistSpecialtyRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SetSpecialtiesAsync(int therapistId, IEnumerable<int> specialtyIds)
    {
        var newIds = specialtyIds.Distinct().ToHashSet();

        var current = await _dbContext.TherapistSpecialties
            .Where(ts => ts.TherapistId == therapistId)
            .ToListAsync();

        var currentIds = current.Select(ts => ts.SpecialtyId).ToHashSet();

        var toRemove = current.Where(ts => !newIds.Contains(ts.SpecialtyId)).ToList();
        var toAdd = newIds.Where(id => !currentIds.Contains(id))
            .Select(id => new TherapistSpecialty
            {
                TherapistId = therapistId,
                SpecialtyId = id
            })
            .ToList();

        if (toRemove.Count > 0)
            _dbContext.TherapistSpecialties.RemoveRange(toRemove);

        if (toAdd.Count > 0)
            await _dbContext.TherapistSpecialties.AddRangeAsync(toAdd);

        await _dbContext.SaveChangesAsync();
    }

    public async Task<IEnumerable<int>> GetValidSpecialtyIdsAsync(IEnumerable<int> candidateIds)
    {
        var ids = candidateIds.Distinct().ToList();
        return await _dbContext.SpecialtyTypes
            .Where(st => ids.Contains(st.Id))
            .Select(st => st.Id)
            .ToListAsync();
    }
}
