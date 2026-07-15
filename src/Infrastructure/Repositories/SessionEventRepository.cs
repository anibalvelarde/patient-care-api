using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class SessionEventRepository(ApplicationDbContext dbContext) :
    EfRepository<SessionEvent>(dbContext), ISessionEventRepository
{
    public override async Task<IReadOnlyList<SessionEvent>> GetAllAsync()
    {
        var result = await _dbContext.TherapySessions
            .Where(ts => (ts.Patient != null) &&
                         (ts.Therapist != null))
            .Include(ts => ts.Patient)
                .ThenInclude(p => p!.User)
            .Include(ts => ts.Patient)
                .ThenInclude(p => p!.Caretakers)
                    .ThenInclude(pc => pc.Caretaker)
                        .ThenInclude(c => c!.User)
            .Include(ts => ts.Therapist)
                .ThenInclude(t => t!.User)
            .Include(ts => ts.AppointmentStatus)
            .Include(ts => ts.Site)
            .Include(ts => ts.SpecialtyType)
            .Select(ts => ExtractSessionEvent(ts))
            .ToListAsync();
        return result;
    }

    public async Task<IReadOnlyList<SessionEvent>> GetAllByTargetDateAsync(DateOnly targetDate)
    {
        var result = await _dbContext.TherapySessions
            .Where(ts => (ts.Patient != null) &&
                         (ts.Therapist != null) &&
                         (ts.SessionDate == targetDate))
            .Include(ts => ts.Patient)
                .ThenInclude(p => p!.User)
            .Include(ts => ts.Patient)
                .ThenInclude(p => p!.Caretakers)
                    .ThenInclude(pc => pc.Caretaker)
                        .ThenInclude(c => c!.User)
            .Include(ts => ts.Therapist)
                .ThenInclude(t => t!.User)
            .Include(ts => ts.AppointmentStatus)
            .Include(ts => ts.Site)
            .Include(ts => ts.SpecialtyType)
            .Select(ts => ExtractSessionEvent(ts))
            .ToListAsync();
        return result;
    }

    public Task<IReadOnlyList<SessionEvent>> GetAllPastDueAsync()
        => GetAllPastDueAsync(patientId: null, therapistId: null);

    public async Task<IReadOnlyList<SessionEvent>> GetAllPastDueAsync(int? patientId, int? therapistId)
    {
        // WP-29 (U3): the past-due predicate runs in SQL instead of materializing the whole
        // table. Date filter is date-only (SessionDate <= today − 35d) — a strict superset of
        // the exact date+time GetPastDue() cutoff, so boundary rows can arrive with
        // IsPastDue == false; callers (SessionEventHandler) apply the exact filter. The money
        // predicate (Amount − Discount − AmountPaid > 0) matches TherapySession.AmountDue()
        // exactly. Includes and the client-side projection now run over dozens of rows, not 11k.
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-TherapySession.DAYS_LATE_LIMIT));

        var query = _dbContext.TherapySessions
            .Where(ts => (ts.Patient != null) &&
                         (ts.Therapist != null) &&
                         (ts.Amount - ts.DiscountAmount - ts.AmountPaid) > 0 &&
                         ts.SessionDate <= cutoff);

        if (patientId.HasValue)
            query = query.Where(ts => ts.PatientId == patientId.Value);
        if (therapistId.HasValue)
            query = query.Where(ts => ts.TherapistId == therapistId.Value);

        var result = await query
            .Include(ts => ts.Patient)
                .ThenInclude(p => p!.User)
            .Include(ts => ts.Patient)
                .ThenInclude(p => p!.Caretakers)
                    .ThenInclude(pc => pc.Caretaker)
                        .ThenInclude(c => c!.User)
            .Include(ts => ts.Therapist)
                .ThenInclude(t => t!.User)
            .Include(ts => ts.AppointmentStatus)
            .Include(ts => ts.Site)
            .Include(ts => ts.SpecialtyType)
            .Select(ts => ExtractSessionEvent(ts))
            .ToListAsync();
        return result;
    }

    public async Task<PagedResult<SessionEvent>> GetByPatientIdAsync(int patientId, int page, int pageSize, bool? isDiscovery = null, string? status = null)
    {
        var query = _dbContext.TherapySessions
            .Where(ts => ts.PatientId == patientId &&
                         ts.Patient != null &&
                         ts.Therapist != null);

        if (isDiscovery.HasValue)
            query = query.Where(ts => ts.SpecialtyType != null && ts.SpecialtyType.IsDiscovery == isDiscovery.Value);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(ts => ts.AppointmentStatus != null && ts.AppointmentStatus.Name == status);

        // COUNT(*) over the filters only — no Includes, so no join amplification.
        var totalCount = await query.CountAsync();

        // WP-21: newest first with deterministic tiebreaks so pages are stable. Ordering and
        // Skip/Take sit on TherapySession columns and run in SQL; ExtractSessionEvent below is
        // client-evaluated, so paging MUST stay ahead of that projection.
        var items = await query
            .OrderByDescending(ts => ts.SessionDate)
            .ThenByDescending(ts => ts.SessionTime)
            .ThenByDescending(ts => ts.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(ts => ts.Patient)
                .ThenInclude(p => p!.User)
            .Include(ts => ts.Patient)
                .ThenInclude(p => p!.Caretakers)
                    .ThenInclude(pc => pc.Caretaker)
                        .ThenInclude(c => c!.User)
            .Include(ts => ts.Therapist)
                .ThenInclude(t => t!.User)
            .Include(ts => ts.AppointmentStatus)
            .Include(ts => ts.Site)
            .Include(ts => ts.SpecialtyType)
            .Select(ts => ExtractSessionEvent(ts))
            .ToListAsync();

        return new PagedResult<SessionEvent>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public override async Task<SessionEvent?> GetByIdAsync(int id)
    {
        var result = await _dbContext.TherapySessions
        .Where(ts => ts.Id == id)
            .Include(ts => ts.Patient)
                .ThenInclude(p => p!.User)
            .Include(ts => ts.Patient)
                .ThenInclude(p => p!.Caretakers)
                    .ThenInclude(pc => pc.Caretaker)
                        .ThenInclude(c => c!.User)
            .Include(ts => ts.Therapist)
                .ThenInclude(t => t!.User)
            .Include(ts => ts.AppointmentStatus)
            .Include(ts => ts.Site)
            .Include(ts => ts.SpecialtyType)
            .Select(ts => ExtractSessionEvent(ts))
            .ToListAsync();
        return result.FirstOrDefault();
    }

    public override async Task<SessionEvent> AddAsync(SessionEvent sessionEvent)
    {
        return await Task.FromException<SessionEvent>(new NotImplementedException());
    }

    public async Task<SessionEvent> UpdateAsync(int therapySessionId, SessionEventUpdateRequest updateRequest)
    {
        // fetch the therapy session
        var therapySessionToUpdate = await _dbContext.TherapySessions
            .Include(ts => ts.Patient)
                .ThenInclude(p => p!.User)
            .Include(ts => ts.Patient)
                .ThenInclude(p => p!.Caretakers)
                    .ThenInclude(pc => pc.Caretaker)
                        .ThenInclude(c => c!.User)
            .Include(ts => ts.Therapist)
                .ThenInclude(t => t!.User)
            .Include(ts => ts.AppointmentStatus)
            .Include(ts => ts.Site)
            .Include(ts => ts.SpecialtyType)
            .FirstAsync(ts => ts.Id == therapySessionId);
        therapySessionToUpdate = MapUpdatedTherapySession(updateRequest, therapySessionToUpdate);
        _dbContext.ChangeTracker.DetectChanges();
        await _dbContext.SaveChangesAsync();
        
        return ExtractSessionEvent(therapySessionToUpdate);
    }

    private TherapySession MapUpdatedTherapySession(SessionEventUpdateRequest updateRequest, TherapySession therapySessionToUpdate)
    {

        therapySessionToUpdate.Duration = updateRequest.Duration;
        therapySessionToUpdate.Amount = updateRequest.Amount;
        therapySessionToUpdate.AmountPaid = updateRequest.AmountPaid;
        therapySessionToUpdate.DiscountAmount = updateRequest.Discount;
        therapySessionToUpdate.ProviderAmount = updateRequest.ProviderAmount;
        therapySessionToUpdate.Notes = updateRequest.Notes;
        therapySessionToUpdate.TherapyTypes = updateRequest.TherapyType;
        if (updateRequest.AppointmentStatusId.HasValue)
        {
            therapySessionToUpdate.AppointmentStatusId = updateRequest.AppointmentStatusId.Value;
        }
        if (updateRequest.TherapistId.HasValue)
        {
            therapySessionToUpdate.TherapistId = updateRequest.TherapistId.Value;
        }
        if (updateRequest.SiteId.HasValue)
        {
            therapySessionToUpdate.SiteId = updateRequest.SiteId.Value;
        }
        if (updateRequest.SpecialtyTypeId.HasValue)
        {
            therapySessionToUpdate.SpecialtyTypeId = updateRequest.SpecialtyTypeId.Value;
        }

        return therapySessionToUpdate;
    }

    private static readonly HashSet<int> ConfirmedStatuses = [2, 4, 6, 7]; // Confirmed, Completed, CheckedIn, InTherapy

    private static SessionEvent ExtractSessionEvent(TherapySession ts)
    {
        ArgumentNullException.ThrowIfNull(ts, nameof(ts) + " must not be null");

        var statusName = ts.AppointmentStatus?.Name ?? "Completed";

        // B3: surface the primary caretaker's contact info (first link as fallback).
        var caretakerUser = ts.Patient!.Caretakers
            .OrderByDescending(pc => pc.PrimaryCaretaker)
            .Select(pc => pc.Caretaker?.User)
            .FirstOrDefault(u => u != null);

        return new SessionEvent
        {
            SessionId = ts.Id,
            PatientId = ts.PatientId,
            TherapistId = ts.TherapistId,
            SessionDate = ts.SessionDate,
            SessionTime = ts.SessionTime,
            Patient = ts.Patient!.User!.GetFullName(),
            Therapist = ts.Therapist!.User!.GetFullName(),
            TherapyTypes = ts.TherapyTypes,
            Amount = ts.Amount,
            Discount = ts.DiscountAmount,
            AmountPaid = ts.AmountPaid,
            AmountDue = ts.AmountDue(),
            IsPaidOff = ts.IsPaidOff,
            IsPastDue = ts.GetPastDue(),
            Notes = ts.Notes,
            AppointmentStatusId = ts.AppointmentStatusId,
            StatusName = statusName,
            IsConfirmed = ConfirmedStatuses.Contains(ts.AppointmentStatusId),
            SiteId = ts.SiteId,
            SiteName = ts.Site?.SiteName,
            SpecialtyTypeId = ts.SpecialtyTypeId,
            SpecialtyAbbreviation = ts.SpecialtyType?.Abbreviation,
            SpecialtyName = ts.SpecialtyType?.Name,
            IsDiscovery = ts.SpecialtyType?.IsDiscovery,
            CaretakerName = caretakerUser?.GetFullName(),
            CaretakerPhone = caretakerUser?.PhoneNumber,
            CaretakerEmail = caretakerUser?.Email,
            ProviderAmount = ts.ProviderAmount,
        };
    }
}