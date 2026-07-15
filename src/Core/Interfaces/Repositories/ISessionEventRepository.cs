using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.BusinessObjects.Therapists;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface ISessionEventRepository : IRepository<SessionEvent>
{
    public Task<IReadOnlyList<SessionEvent>> GetAllByTargetDateAsync(DateOnly targetDate);
    // WP-29 (U3): returns past-due CANDIDATES filtered in SQL (money owed + date-only cutoff —
    // a strict superset of the exact date+time TherapySession.GetPastDue predicate). Callers
    // must still apply the exact IsPastDue filter; SessionEventHandler does.
    public Task<IReadOnlyList<SessionEvent>> GetAllPastDueAsync();
    public Task<IReadOnlyList<SessionEvent>> GetAllPastDueAsync(int? patientId, int? therapistId);
    // WP-21 (F1): paged, newest first (SessionDate/SessionTime/SessionID DESC).
    public Task<PagedResult<SessionEvent>> GetByPatientIdAsync(int patientId, int page, int pageSize, bool? isDiscovery = null, string? status = null);
    public Task<SessionEvent> UpdateAsync(int therapySessionId, SessionEventUpdateRequest updateRequest);
}