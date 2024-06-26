using Neurocorp.Api.Core.BusinessObjects.Sessions;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface IHandleSessionEvent : IService<SessionEvent>
{
    public Task<IEnumerable<SessionEvent>> GetAllByTargetDateAsync(DateOnly targetDate);
    public Task<IEnumerable<SessionEvent>> GetAllPastDueAsync();
    public Task<SessionEvent> CreateAsync(SessionEventRequest request);
    public Task<bool> UpdateAsync(int SessionEventId, SessionEventUpdateRequest request);
    public Task<bool> VerifyRequestAsync(int sessionAggId, SessionEventUpdateRequest request);
}