using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Services;

public class BookingService : IBookingService
{
    private static readonly HashSet<int> ConfirmedStatuses = [2, 4, 6, 7]; // Confirmed, Completed, CheckedIn, InTherapy
    private static readonly HashSet<int> ActiveStatuses = [1, 2, 6, 7]; // Proposed, Confirmed, CheckedIn, InTherapy

    private readonly ILogger<BookingService> _logger;
    private readonly IBookingRepository _bookingRepository;
    private readonly ITherapySessionRepository _therapySessionRepository;
    private readonly ISessionTransitionMoneyService _transitionMoney;

    public BookingService(ILogger<BookingService> logger, IBookingRepository bookingRepository,
        // WP-42: the money-at-transition choke point needs the raw entity (guards + originals),
        // hence the session repository alongside the SessionEvent-shaped booking repository.
        ITherapySessionRepository therapySessionRepository,
        ISessionTransitionMoneyService transitionMoney)
    {
        _logger = logger;
        _bookingRepository = bookingRepository;
        _therapySessionRepository = therapySessionRepository;
        _transitionMoney = transitionMoney;
    }

    public async Task<IEnumerable<LookupItem>> GetStatusesAsync()
    {
        _logger.LogInformation("Fetching all appointment statuses");
        return await _bookingRepository.GetAllStatusesAsync();
    }

    public async Task<SessionEvent> UpdateStatusAsync(int sessionId, int statusId)
    {
        _logger.LogInformation("Updating session {SessionId} to status {StatusId}", sessionId, statusId);
        // WP-42: transitions into Cancelled (3) / NoShow (5) carry money side-effects — guards
        // run first (throwing 400 before anything is written), the patch applies atomically
        // with the status write. Other statuses pass through untouched (patch = null).
        var patch = await PrepareTransitionAsync(sessionId, statusId);
        return await _bookingRepository.UpdateStatusAsync(sessionId, statusId, patch);
    }

    public async Task<SessionEvent> ConfirmAppointmentAsync(int sessionId, ConfirmationRequest request)
    {
        _logger.LogInformation("Recording confirmation for session {SessionId}: {Method} -> {Result}",
            sessionId, request.ConfirmationMethod, request.ConfirmationResult);

        var result = request.ConfirmationResult;

        // WP-42: Declined ≡ Cancelled — the full cancel money semantics + guards apply, and the
        // guard runs BEFORE the confirmation attempt is recorded (per contract: a blocked
        // Declined leaves NO AppointmentConfirmation row).
        if (string.Equals(result, "Declined", StringComparison.OrdinalIgnoreCase))
        {
            var patch = await PrepareTransitionAsync(sessionId, 3);
            await _bookingRepository.AddConfirmationAsync(sessionId, request);
            return await _bookingRepository.UpdateStatusAsync(sessionId, 3, patch); // Cancelled
        }

        // Create confirmation record
        await _bookingRepository.AddConfirmationAsync(sessionId, request);

        if (string.Equals(result, "Confirmed", StringComparison.OrdinalIgnoreCase))
        {
            return await _bookingRepository.UpdateStatusAsync(sessionId, 2); // Confirmed
        }

        // NoAnswer, LeftMessage — just return current state without changing status
        return await _bookingRepository.GetSessionEventByIdAsync(sessionId)
            ?? throw new ArgumentException($"Session {sessionId} not found.");
    }

    public async Task<SessionEvent> CancelAppointmentAsync(int sessionId, string? reason)
    {
        _logger.LogInformation("Cancelling session {SessionId}", sessionId);

        // WP-42: guards first — when blocked, the cancel reason is NOT appended either
        // (nothing changes on a blocked transition).
        var patch = await PrepareTransitionAsync(sessionId, 3);

        if (!string.IsNullOrWhiteSpace(reason))
        {
            await _bookingRepository.AppendNotesAsync(sessionId, $"[Cancelled] {reason}");
        }

        return await _bookingRepository.UpdateStatusAsync(sessionId, 3, patch); // Cancelled
    }

    // WP-42: shared entry to the money-at-transition choke point for the booking-lifecycle
    // paths (/cancel, /noshow, /confirm→Declined). Null when the target status carries no
    // money semantics or the transition is an idempotent re-entry.
    private async Task<SessionTransitionMoneyPatch?> PrepareTransitionAsync(int sessionId, int statusId)
    {
        if (statusId != SessionTransitionMoneyService.CancelledStatusId
            && statusId != SessionTransitionMoneyService.NoShowStatusId)
        {
            return null;
        }

        var session = await _therapySessionRepository.GetByIdAsync(sessionId)
            ?? throw new ArgumentException($"Session {sessionId} not found.");
        return await _transitionMoney.PrepareTransitionAsync(session, statusId);
    }

    public async Task<IEnumerable<SessionEvent>> GetUnconfirmedAsync(DateOnly from, DateOnly to)
    {
        _logger.LogInformation("Fetching unconfirmed sessions from {From} to {To}", from, to);
        return await _bookingRepository.GetByStatusAndDateRangeAsync(1, from, to); // Proposed
    }

    public async Task<IEnumerable<SessionEvent>> GetUpcomingAsync(DateOnly from, DateOnly to)
    {
        _logger.LogInformation("Fetching upcoming sessions from {From} to {To}", from, to);
        return await _bookingRepository.GetByStatusesAndDateRangeAsync(ActiveStatuses, from, to);
    }

    public async Task<IEnumerable<ConfirmationRecord>> GetConfirmationsAsync(int sessionId)
    {
        _logger.LogInformation("Fetching confirmations for session {SessionId}", sessionId);
        return await _bookingRepository.GetConfirmationsAsync(sessionId);
    }
}
