using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Sessions;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface IBookingService
{
    Task<IEnumerable<LookupItem>> GetStatusesAsync();
    Task<SessionEvent> UpdateStatusAsync(int sessionId, int statusId);
    Task<SessionEvent> ConfirmAppointmentAsync(int sessionId, ConfirmationRequest request);
    Task<SessionEvent> CancelAppointmentAsync(int sessionId, string? reason);
    Task<IEnumerable<SessionEvent>> GetUnconfirmedAsync(DateOnly from, DateOnly to);
    Task<IEnumerable<SessionEvent>> GetUpcomingAsync(DateOnly from, DateOnly to);
    Task<IEnumerable<ConfirmationRecord>> GetConfirmationsAsync(int sessionId);
}
