using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.BusinessObjects.ServicePayments;
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

    // WP-29 (U3): batched HasCompletedDiscoveryAsync — one grouped query for N patients.
    // Predicate mirrors HasCompletedDiscoveryAsync above; keep the two in step.
    public async Task<IReadOnlyList<int>> GetPatientIdsWithCompletedDiscoveryAsync(IReadOnlyCollection<int> patientIds)
    {
        if (patientIds.Count == 0) return [];

        return await _dbContext.TherapySessions
            .Where(s => patientIds.Contains(s.PatientId)
                && s.AppointmentStatusId == 4
                && s.SpecialtyType != null
                && s.SpecialtyType.IsDiscovery)
            .Select(s => s.PatientId)
            .Distinct()
            .ToListAsync();
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

    public async Task<IReadOnlyList<TherapySession>> GetByTherapistAndDateRangeAsync(int therapistId, DateOnly fromDate, DateOnly toDate)
    {
        return await _dbContext.TherapySessions
            .Where(s => s.TherapistId == therapistId
                && s.SessionDate >= fromDate
                && s.SessionDate <= toDate
                && s.AppointmentStatusId != 3) // Exclude cancelled
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TherapySession>> GetByTherapistIdAndDateRangeAsync(
        int therapistId, DateOnly from, DateOnly to, IEnumerable<int> statusIds)
    {
        var statusIdList = statusIds.ToList();
        return await _dbContext.TherapySessions
            .Include(s => s.Patient).ThenInclude(p => p!.User)
            .Include(s => s.SpecialtyType)
            .Include(s => s.AppointmentStatus)
            .Where(s => s.TherapistId == therapistId
                && s.SessionDate >= from
                && s.SessionDate <= to
                && statusIdList.Contains(s.AppointmentStatusId))
            .OrderBy(s => s.SessionDate)
            .ThenBy(s => s.SessionTime)
            .ToListAsync();
    }

    // WP-29 (U3): slim money rows for the pending-payroll summary/report — no includes, and the
    // allocation sum is a correlated scalar subquery (translates to SQL on every provider incl.
    // InMemory — same precedent as PatientProfileRepository.GetSessionHistoryAsync). The owing
    // filter runs in SQL so only rows that still owe the therapist are materialized.
    public async Task<IReadOnlyList<OwedProviderSessionRow>> GetOwedProviderSessionRowsAsync(
        DateOnly from, DateOnly to, IEnumerable<int> statusIds)
    {
        var statusIdList = statusIds.ToList();
        return await _dbContext.TherapySessions
            .Where(s => s.SessionDate >= from
                && s.SessionDate <= to
                && statusIdList.Contains(s.AppointmentStatusId))
            .Select(s => new OwedProviderSessionRow
            {
                SessionId = s.Id,
                TherapistId = s.TherapistId,
                PatientId = s.PatientId,
                SessionDate = s.SessionDate,
                Amount = s.Amount,
                ProviderAmount = s.ProviderAmount,
                Applied = _dbContext.SessionServicePayments
                    .Where(a => a.TherapySessionId == s.Id)
                    .Sum(a => (decimal?)a.AmountApplied) ?? 0m,
            })
            .Where(r => r.ProviderAmount - r.Applied > 0)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TherapySession>> GetByTreatmentPlanIdAsync(int treatmentPlanId)
    {
        return await _dbContext.TherapySessions
            .Include(s => s.Therapist).ThenInclude(t => t!.User)
            .Include(s => s.SpecialtyType)
            .Include(s => s.AppointmentStatus)
            .Where(s => s.TreatmentPlanLineId != null
                && s.TreatmentPlanLine!.TreatmentPlanId == treatmentPlanId)
            .OrderBy(s => s.SessionDate)
            .ThenBy(s => s.SessionTime)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<int>> GetTherapistIdsForPatientAsync(int patientId)
    {
        return await _dbContext.TherapySessions
            .Where(s => s.PatientId == patientId && s.AppointmentStatusId != 3)
            .GroupBy(s => s.TherapistId)
            .OrderByDescending(g => g.Max(s => s.SessionDate))
            .Select(g => g.Key)
            .ToListAsync();
    }

    public async Task AddRangeAsync(IEnumerable<TherapySession> sessions)
    {
        await _dbContext.TherapySessions.AddRangeAsync(sessions);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<bool> HasSessionsForPlanAsync(int treatmentPlanId)
    {
        return await _dbContext.TherapySessions
            .AnyAsync(s => s.TreatmentPlanLineId != null
                && s.TreatmentPlanLine!.TreatmentPlanId == treatmentPlanId
                && s.AppointmentStatusId != 3); // Exclude cancelled
    }

    public async Task<IReadOnlyList<TherapySession>> GetBySiteAndDateRangeAsync(int siteId, DateOnly from, DateOnly to)
    {
        return await _dbContext.TherapySessions
            .Include(s => s.Patient).ThenInclude(p => p!.User)
            .Include(s => s.Therapist).ThenInclude(t => t!.User)
            .Include(s => s.SpecialtyType)
            .Include(s => s.AppointmentStatus)
            .Where(s => s.SiteId == siteId
                && s.SessionDate >= from
                && s.SessionDate <= to
                && s.AppointmentStatusId != 3) // Exclude cancelled
            .OrderBy(s => s.SessionDate)
            .ThenBy(s => s.SessionTime)
            .ToListAsync();
    }

    // WP-49 (BR3). The predicate runs in SQL over ~11k rows and is deliberately UNINDEXED —
    // the existing 35-day past-due predicate scans the same table the same way, and indexing
    // one without the other would imply a performance story that isn't true.
    //
    // ⚠️ This mirrors SessionFeeService's write-time eligibility check. The two must agree, or
    // the preview shows rows the batch then refuses to charge. Keep them in step.
    public async Task<IReadOnlyList<TherapySession>> GetLateFeeEligibleAsync(DateOnly asOf, int graceDays)
    {
        // Eligible on the day AFTER the grace period expires: 6 grace days ⇒ charged on day 7.
        var cutoff = asOf.AddDays(-(graceDays + 1));

        return await _dbContext.TherapySessions
            .Include(s => s.Patient).ThenInclude(p => p!.User)
            .Include(s => s.Patient).ThenInclude(p => p!.Caretakers).ThenInclude(pc => pc.Caretaker).ThenInclude(c => c!.User)
            .Where(s => s.LateFeeAppliedOn == null                       // the anti-compounding latch
                && (s.Amount - s.DiscountAmount - s.AmountPaid + (s.OnSiteChargeAmount ?? 0m)) > 0
                && s.SessionDate <= cutoff
                && s.AppointmentStatusId != 3)                           // Cancelled is not billable
                                                                         // NoShow (5) IS eligible — an unpaid
                                                                         // no-show fee can itself go late.
            .OrderBy(s => s.SessionDate)
            .ThenBy(s => s.SessionTime)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TherapySession>> GetByIdsWithPartiesAsync(IReadOnlyCollection<int> sessionIds)
    {
        if (sessionIds.Count == 0) return [];

        return await _dbContext.TherapySessions
            .Include(s => s.Patient).ThenInclude(p => p!.User)
            .Include(s => s.Patient).ThenInclude(p => p!.Caretakers).ThenInclude(pc => pc.Caretaker).ThenInclude(c => c!.User)
            .Where(s => sessionIds.Contains(s.Id))
            .OrderBy(s => s.SessionDate)
            .ThenBy(s => s.SessionTime)
            .ToListAsync();
    }
}
