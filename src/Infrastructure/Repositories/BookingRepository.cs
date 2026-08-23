using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class BookingRepository : IBookingRepository
{
    private readonly ApplicationDbContext _dbContext;

    public BookingRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<LookupItem>> GetAllStatusesAsync()
    {
        return await _dbContext.AppointmentStatuses
            .OrderBy(s => s.Id)
            .Select(s => new LookupItem
            {
                Id = s.Id,
                Abbreviation = s.Abbreviation,
                Name = s.Name,
                Description = s.Description,
                SortOrder = s.SortOrder,
                CreatedTimestamp = s.CreatedTimestamp,
                LastUpdatedTimestamp = s.LastUpdatedTimestamp,
            })
            .ToListAsync();
    }

    public async Task<SessionEvent?> GetSessionEventByIdAsync(int sessionId)
    {
        return await _dbContext.TherapySessions
            .Where(ts => ts.Id == sessionId)
            .Include(ts => ts.Patient).ThenInclude(p => p!.User)
            .Include(ts => ts.Patient).ThenInclude(p => p!.Caretakers)
                .ThenInclude(pc => pc.Caretaker).ThenInclude(c => c!.User)
            .Include(ts => ts.Therapist).ThenInclude(t => t!.User)
            .Include(ts => ts.AppointmentStatus)
            .Include(ts => ts.Site)
            .Include(ts => ts.SpecialtyType)
            .Select(ts => MapToSessionEvent(ts))
            .FirstOrDefaultAsync();
    }

    public async Task<SessionEvent> UpdateStatusAsync(int sessionId, int statusId, SessionTransitionMoneyPatch? moneyPatch = null)
    {
        var session = await _dbContext.TherapySessions
            .Include(ts => ts.Patient).ThenInclude(p => p!.User)
            .Include(ts => ts.Patient).ThenInclude(p => p!.Caretakers)
                .ThenInclude(pc => pc.Caretaker).ThenInclude(c => c!.User)
            .Include(ts => ts.Therapist).ThenInclude(t => t!.User)
            .Include(ts => ts.AppointmentStatus)
            .Include(ts => ts.Site)
            .Include(ts => ts.SpecialtyType)
            .FirstOrDefaultAsync(ts => ts.Id == sessionId)
            ?? throw new ArgumentException($"Session {sessionId} not found.");

        session.AppointmentStatusId = statusId;

        // WP-42: apply the transition money side-effect (zero-at-cancel / no-show fee) in the
        // same SaveChanges as the status write, appending the audit marker to Notes.
        if (moneyPatch is not null)
        {
            session.Amount = moneyPatch.Amount;
            session.DiscountAmount = moneyPatch.DiscountAmount;
            session.ProviderAmount = moneyPatch.ProviderAmount;
            session.GrossProfit = moneyPatch.GrossProfit;
            if (!string.IsNullOrEmpty(moneyPatch.NotesMarker))
            {
                session.Notes = string.IsNullOrWhiteSpace(session.Notes)
                    ? moneyPatch.NotesMarker
                    : $"{session.Notes}\n{moneyPatch.NotesMarker}";
            }
        }

        // Reload the navigation property for the new status
        var newStatus = await _dbContext.AppointmentStatuses.FindAsync(statusId)
            ?? throw new ArgumentException($"Status {statusId} not found.");
        session.AppointmentStatus = newStatus;

        await _dbContext.SaveChangesAsync();

        return MapToSessionEvent(session);
    }

    public async Task AppendNotesAsync(int sessionId, string note)
    {
        var session = await _dbContext.TherapySessions.FindAsync(sessionId)
            ?? throw new ArgumentException($"Session {sessionId} not found.");

        session.Notes = string.IsNullOrWhiteSpace(session.Notes)
            ? note
            : $"{session.Notes}\n{note}";

        await _dbContext.SaveChangesAsync();
    }

    public async Task AddConfirmationAsync(int sessionId, ConfirmationRequest request)
    {
        var confirmation = new AppointmentConfirmation
        {
            SessionId = sessionId,
            ConfirmationMethod = request.ConfirmationMethod,
            ConfirmationResult = request.ConfirmationResult,
            ConfirmedAt = DateTime.UtcNow,
            Notes = request.Notes,
        };

        await _dbContext.AppointmentConfirmations.AddAsync(confirmation);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<IEnumerable<SessionEvent>> GetByStatusAndDateRangeAsync(int statusId, DateOnly from, DateOnly to)
    {
        return await _dbContext.TherapySessions
            .Where(ts => ts.AppointmentStatusId == statusId
                && ts.SessionDate >= from && ts.SessionDate <= to
                && ts.Patient != null && ts.Therapist != null)
            .Include(ts => ts.Patient).ThenInclude(p => p!.User)
            .Include(ts => ts.Patient).ThenInclude(p => p!.Caretakers)
                .ThenInclude(pc => pc.Caretaker).ThenInclude(c => c!.User)
            .Include(ts => ts.Therapist).ThenInclude(t => t!.User)
            .Include(ts => ts.AppointmentStatus)
            .Include(ts => ts.Site)
            .Include(ts => ts.SpecialtyType)
            .OrderBy(ts => ts.SessionDate)
            .ThenBy(ts => ts.SessionTime)
            .Select(ts => MapToSessionEvent(ts))
            .ToListAsync();
    }

    public async Task<IEnumerable<SessionEvent>> GetByStatusesAndDateRangeAsync(IEnumerable<int> statusIds, DateOnly from, DateOnly to)
    {
        var statusList = statusIds.ToList();
        return await _dbContext.TherapySessions
            .Where(ts => statusList.Contains(ts.AppointmentStatusId)
                && ts.SessionDate >= from && ts.SessionDate <= to
                && ts.Patient != null && ts.Therapist != null)
            .Include(ts => ts.Patient).ThenInclude(p => p!.User)
            .Include(ts => ts.Patient).ThenInclude(p => p!.Caretakers)
                .ThenInclude(pc => pc.Caretaker).ThenInclude(c => c!.User)
            .Include(ts => ts.Therapist).ThenInclude(t => t!.User)
            .Include(ts => ts.AppointmentStatus)
            .Include(ts => ts.Site)
            .Include(ts => ts.SpecialtyType)
            .OrderBy(ts => ts.SessionDate)
            .ThenBy(ts => ts.SessionTime)
            .Select(ts => MapToSessionEvent(ts))
            .ToListAsync();
    }

    public async Task<IEnumerable<ConfirmationRecord>> GetConfirmationsAsync(int sessionId)
    {
        return await _dbContext.AppointmentConfirmations
            .Where(c => c.SessionId == sessionId)
            .OrderByDescending(c => c.ConfirmedAt)
            .Select(c => new ConfirmationRecord
            {
                ConfirmationId = c.Id,
                SessionId = c.SessionId,
                ConfirmationMethod = c.ConfirmationMethod,
                ConfirmationResult = c.ConfirmationResult,
                ConfirmedAt = c.ConfirmedAt,
                Notes = c.Notes,
                CreatedTimestamp = c.CreatedTimestamp,
            })
            .ToListAsync();
    }

    private static SessionEvent MapToSessionEvent(TherapySession ts)
    {
        var statusName = ts.AppointmentStatus?.Name ?? SessionStatus.Names.Completed;

        // B3: surface the primary caretaker's contact info (first link as fallback) —
        // keep in step with SessionEventRepository.ExtractSessionEvent.
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
            IsConfirmed = SessionStatus.ConfirmedStatuses.Contains(ts.AppointmentStatusId),
            SiteId = ts.SiteId,
            SiteName = ts.Site?.SiteName,
            SpecialtyTypeId = ts.SpecialtyTypeId,
            SpecialtyAbbreviation = ts.SpecialtyType?.Abbreviation,
            SpecialtyName = ts.SpecialtyType?.Name,
            IsDiscovery = ts.SpecialtyType?.IsDiscovery,
            CaretakerName = caretakerUser?.GetFullName(),
            CaretakerPhone = caretakerUser?.PhoneNumber,
            CaretakerEmail = caretakerUser?.Email,
            // WP-40: on-site trip charge snapshot (null = in-clinic).
            OnSiteChargeAmount = ts.OnSiteChargeAmount,
            // WP-49 (BR3/BR4): fee state — CarriesFee drives the discount-edit loophole guard.
            LateFeeAmount = ts.LateFeeAmount,
            LateFeeAppliedOn = ts.LateFeeAppliedOn,
            FeeWaivedOn = ts.FeeWaivedOn,
            CarriesFee = ts.CarriesFee(),
            // WP-31 (U1): audit from the session's own trio; updater name resolved in the handler.
            // KEEP IN STEP with SessionEventRepository.ExtractSessionEvent (the two-mapper contract).
            Audit = AuditInfo.FromEntity(ts),
        };
    }
}
