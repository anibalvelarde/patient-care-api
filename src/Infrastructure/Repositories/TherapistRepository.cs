using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class TherapistRepository(ApplicationDbContext dbContext) :
    EfRepository<Therapist>(dbContext), ITherapistRepository
{
    public async Task<IReadOnlyList<Therapist>> GetByIdsWithUserAsync(IEnumerable<int> therapistIds)
    {
        var ids = therapistIds.Distinct().ToList();
        return await _dbContext.Therapists
            .Include(t => t.User)
            .Where(t => ids.Contains(t.Id))
            .ToListAsync();
    }
}
