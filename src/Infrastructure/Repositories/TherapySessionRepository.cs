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
            .Include(s => s.Patient).ThenInclude(p => p!.User)
            .Include(s => s.Therapist).ThenInclude(t => t!.User)
            .Where(s => patientIds.Contains(s.PatientId) && !s.IsPaidOff)
            .OrderBy(s => s.SessionDate)
            .ThenBy(s => s.SessionTime)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TherapySession>> GetByPatientIdsAndDateRangeAsync(IEnumerable<int> patientIds, DateOnly from, DateOnly to)
    {
        return await _dbContext.TherapySessions
            .Include(s => s.Patient).ThenInclude(p => p!.User)
            .Include(s => s.Therapist).ThenInclude(t => t!.User)
            .Include(s => s.SessionPayments).ThenInclude(sp => sp.Payment).ThenInclude(p => p.PaymentType)
            .Where(s => patientIds.Contains(s.PatientId) && s.SessionDate >= from && s.SessionDate <= to)
            .OrderBy(s => s.SessionDate)
            .ThenBy(s => s.SessionTime)
            .ToListAsync();
    }

    public async Task<bool> HasCompletedDiscoveryAsync(int patientId)
    {
        return await _dbContext.TherapySessions
            .Include(s => s.SpecialtyType)
            .AnyAsync(s => s.PatientId == patientId
                && s.AppointmentStatusId == 4
                && s.SpecialtyType != null
                && s.SpecialtyType.IsDiscovery);
    }

    public async Task<TherapySession?> GetByIdWithSpecialtyAsync(int id)
    {
        return await _dbContext.TherapySessions
            .Include(s => s.SpecialtyType)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<IReadOnlyList<TherapySession>> GetCompletedDiscoverySessionsAsync(int patientId)
    {
        return await _dbContext.TherapySessions
            .Include(s => s.SpecialtyType)
            .Include(s => s.Therapist).ThenInclude(t => t!.User)
            .Where(s => s.PatientId == patientId
                && s.AppointmentStatusId == 4
                && s.SpecialtyType != null
                && s.SpecialtyType.IsDiscovery)
            .OrderByDescending(s => s.SessionDate)
            .ThenByDescending(s => s.SessionTime)
            .ToListAsync();
    }
}
