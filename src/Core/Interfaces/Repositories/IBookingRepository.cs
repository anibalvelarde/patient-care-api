using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Sessions;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface IBookingRepository
{
    Task<IEnumerable<LookupItem>> GetAllStatusesAsync();
    Task<SessionEvent?> GetSessionEventByIdAsync(int sessionId);
    Task<SessionEvent> UpdateStatusAsync(int sessionId, int statusId);
    Task AppendNotesAsync(int sessionId, string note);
    Task AddConfirmationAsync(int sessionId, ConfirmationRequest request);
    Task<IEnumerable<SessionEvent>> GetByStatusAndDateRangeAsync(int statusId, DateOnly from, DateOnly to);
    Task<IEnumerable<SessionEvent>> GetByStatusesAndDateRangeAsync(IEnumerable<int> statusIds, DateOnly from, DateOnly to);
    Task<IEnumerable<ConfirmationRecord>> GetConfirmationsAsync(int sessionId);
}
