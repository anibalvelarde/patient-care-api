using Neurocorp.Api.Core.BusinessObjects.Sessions;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface IBookingService
{
    Task<IEnumerable<AppointmentStatusInfo>> GetStatusesAsync();
    Task<SessionEvent> UpdateStatusAsync(int sessionId, int statusId);
    Task<SessionEvent> ConfirmAppointmentAsync(int sessionId, ConfirmationRequest request);
    Task<SessionEvent> CancelAppointmentAsync(int sessionId, string? reason);
    Task<IEnumerable<SessionEvent>> GetUnconfirmedAsync(DateOnly from, DateOnly to);
    Task<IEnumerable<SessionEvent>> GetUpcomingAsync(int days);
    Task<IEnumerable<ConfirmationRecord>> GetConfirmationsAsync(int sessionId);
}
