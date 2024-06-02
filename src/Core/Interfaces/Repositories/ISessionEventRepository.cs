using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.BusinessObjects.Therapists;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface ISessionEventRepository : IRepository<SessionEvent>
{
    public Task<IReadOnlyList<SessionEvent>> GetAllByTargetDateAsync(DateOnly targetDate);

    public Task<SessionEvent> UpdateAsync(int therapySessionId, SessionEventUpdateRequest updateRequest);
}