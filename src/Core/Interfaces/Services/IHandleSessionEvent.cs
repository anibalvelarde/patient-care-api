using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.BusinessObjects.Sessions;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface IHandleSessionEvent : IService<SessionEvent>
{
    public Task<IEnumerable<SessionEvent>> GetAllByTargetDateAsync(DateOnly targetDate);
    public Task<IEnumerable<SessionEvent>> GetAllPastDueAsync();
    public Task<IEnumerable<PatientPastDueInfo>> GetAllPatientsPastDueAsync();
    public Task<SessionEvent> CreateAsync(SessionEventRequest request);
    // WP-24 (audit F1): true when the request's resolved specialty is a discovery type — backs
    // the imperative Patients.StartDiscovery gate in SessionsController.CreateSession.
    public Task<bool> IsDiscoveryRequestAsync(SessionEventRequest request);
    public Task<bool> UpdateAsync(int SessionEventId, SessionEventUpdateRequest request);
    public Task<bool> VerifyRequestAsync(int sessionAggId, SessionEventUpdateRequest request);
    public Task<IEnumerable<DiscoverySessionSummary>> GetCompletedDiscoverySessionsAsync(int patientId);
}