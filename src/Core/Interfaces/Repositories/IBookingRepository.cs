using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Sessions;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface IBookingRepository
{
    Task<IEnumerable<LookupItem>> GetAllStatusesAsync();
    Task<SessionEvent?> GetSessionEventByIdAsync(int sessionId);
    /// <summary>
    /// Sets the appointment status; when <paramref name="moneyPatch"/> is supplied (WP-42
    /// transitions into 3/5), applies the money side-effect and appends its Notes marker in the
    /// SAME save — the status and money writes are atomic.
    /// </summary>
    Task<SessionEvent> UpdateStatusAsync(int sessionId, int statusId, SessionTransitionMoneyPatch? moneyPatch = null);
    Task AppendNotesAsync(int sessionId, string note);
    Task AddConfirmationAsync(int sessionId, ConfirmationRequest request);
    Task<IEnumerable<SessionEvent>> GetByStatusAndDateRangeAsync(int statusId, DateOnly from, DateOnly to);
    Task<IEnumerable<SessionEvent>> GetByStatusesAndDateRangeAsync(IEnumerable<int> statusIds, DateOnly from, DateOnly to);
    Task<IEnumerable<ConfirmationRecord>> GetConfirmationsAsync(int sessionId);
}
