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

    public async Task<IReadOnlyList<TherapySession>> GetByPatientIdsAndDateRangeAsync(IEnumerable<int> patientIds, DateOnly from, DateOnly to)
    {
        return await _dbContext.TherapySessions
            .Include(s => s.Patient).ThenInclude(p => p.User)
            .Include(s => s.Therapist).ThenInclude(t => t.User)
            .Include(s => s.SessionPayments).ThenInclude(sp => sp.Payment).ThenInclude(p => p.PaymentType)
            .Where(s => patientIds.Contains(s.PatientId) && s.SessionDate >= from && s.SessionDate <= to)
            .OrderBy(s => s.SessionDate)
            .ThenBy(s => s.SessionTime)
            .ToListAsync();
    }
}
