using Microsoft.Extensions.Logging;
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

    public BookingService(ILogger<BookingService> logger, IBookingRepository bookingRepository)
    {
        _logger = logger;
        _bookingRepository = bookingRepository;
    }

    public async Task<IEnumerable<AppointmentStatusInfo>> GetStatusesAsync()
    {
        _logger.LogInformation("Fetching all appointment statuses");
        return await _bookingRepository.GetAllStatusesAsync();
    }

    public async Task<SessionEvent> UpdateStatusAsync(int sessionId, int statusId)
    {
        _logger.LogInformation("Updating session {SessionId} to status {StatusId}", sessionId, statusId);
        return await _bookingRepository.UpdateStatusAsync(sessionId, statusId);
    }

    public async Task<SessionEvent> ConfirmAppointmentAsync(int sessionId, ConfirmationRequest request)
    {
        _logger.LogInformation("Recording confirmation for session {SessionId}: {Method} -> {Result}",
            sessionId, request.ConfirmationMethod, request.ConfirmationResult);

        // Create confirmation record
        await _bookingRepository.AddConfirmationAsync(sessionId, request);

        // Update status based on result
        var result = request.ConfirmationResult;
        if (string.Equals(result, "Confirmed", StringComparison.OrdinalIgnoreCase))
        {
            return await _bookingRepository.UpdateStatusAsync(sessionId, 2); // Confirmed
        }
        else if (string.Equals(result, "Declined", StringComparison.OrdinalIgnoreCase))
        {
            return await _bookingRepository.UpdateStatusAsync(sessionId, 3); // Cancelled
        }

        // NoAnswer, LeftMessage — just return current state without changing status
        return await _bookingRepository.GetSessionEventByIdAsync(sessionId)
            ?? throw new ArgumentException($"Session {sessionId} not found.");
    }

    public async Task<SessionEvent> CancelAppointmentAsync(int sessionId, string? reason)
    {
        _logger.LogInformation("Cancelling session {SessionId}", sessionId);

        if (!string.IsNullOrWhiteSpace(reason))
        {
            await _bookingRepository.AppendNotesAsync(sessionId, $"[Cancelled] {reason}");
        }

        return await _bookingRepository.UpdateStatusAsync(sessionId, 3); // Cancelled
    }

    public async Task<IEnumerable<SessionEvent>> GetUnconfirmedAsync(DateOnly from, DateOnly to)
    {
        _logger.LogInformation("Fetching unconfirmed sessions from {From} to {To}", from, to);
        return await _bookingRepository.GetByStatusAndDateRangeAsync(1, from, to); // Proposed
    }

    public async Task<IEnumerable<SessionEvent>> GetUpcomingAsync(int days)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow);
        var to = from.AddDays(days);
        _logger.LogInformation("Fetching upcoming sessions for next {Days} days", days);
        return await _bookingRepository.GetByStatusesAndDateRangeAsync(ActiveStatuses, from, to);
    }

    public async Task<IEnumerable<ConfirmationRecord>> GetConfirmationsAsync(int sessionId)
    {
        _logger.LogInformation("Fetching confirmations for session {SessionId}", sessionId);
        return await _bookingRepository.GetConfirmationsAsync(sessionId);
    }
}
