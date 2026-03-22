using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class TherapySessionRepository(ApplicationDbContext dbContext) :
    EfRepository<TherapySession>(dbContext), ITherapySessionRepository
{
    public async Task<IReadOnlyList<TherapySession>> GetUnpaidByPatientIdsAsync(IEnumerable<int> patientIds)
    {
        return await _dbContext.TherapySessions
            .Include(s => s.Patient).ThenInclude(p => p.User)
            .Include(s => s.Therapist).ThenInclude(t => t.User)
            .Where(s => patientIds.Contains(s.PatientId) && !s.IsPaidOff)
            .OrderBy(s => s.SessionDate)
            .ThenBy(s => s.SessionTime)
            .ToListAsync();
    }
}
