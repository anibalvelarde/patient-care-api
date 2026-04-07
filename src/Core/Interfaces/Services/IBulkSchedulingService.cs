using Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface IBulkSchedulingService
{
    Task<BulkScheduleResult> ScheduleAsync(int planId, BulkScheduleRequest request);
    Task<PlanProgressSummary> GetProgressAsync(int planId);
    Task<IReadOnlyList<PlanSessionInfo>> GetSessionsAsync(int planId);
}
