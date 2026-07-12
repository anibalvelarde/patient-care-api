using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.BusinessObjects.Therapists;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface ISessionEventRepository : IRepository<SessionEvent>
{
    public Task<IReadOnlyList<SessionEvent>> GetAllByTargetDateAsync(DateOnly targetDate);
    public Task<IReadOnlyList<SessionEvent>> GetAllPastDueAsync();
    // WP-21 (F1): paged, newest first (SessionDate/SessionTime/SessionID DESC).
    public Task<PagedResult<SessionEvent>> GetByPatientIdAsync(int patientId, int page, int pageSize, bool? isDiscovery = null, string? status = null);
    public Task<SessionEvent> UpdateAsync(int therapySessionId, SessionEventUpdateRequest updateRequest);
}