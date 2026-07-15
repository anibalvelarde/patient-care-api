using Neurocorp.Api.Core.BusinessObjects.ServicePayments;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface ITherapySessionRepository : IRepository<TherapySession>
{
    Task<IReadOnlyList<TherapySession>> GetUnpaidByPatientIdsAsync(IEnumerable<int> patientIds);
    Task<IReadOnlyList<TherapySession>> GetByPatientIdsAndDateRangeAsync(IEnumerable<int> patientIds, DateOnly from, DateOnly to);
    Task<bool> HasCompletedDiscoveryAsync(int patientId);
    // WP-29 (U3): batched variant — which of these patients have a completed discovery session.
    Task<IReadOnlyList<int>> GetPatientIdsWithCompletedDiscoveryAsync(IReadOnlyCollection<int> patientIds);
    Task<TherapySession?> GetByIdWithSpecialtyAsync(int id);
    Task<IReadOnlyList<TherapySession>> GetCompletedDiscoverySessionsAsync(int patientId);
    Task<IReadOnlyList<TherapySession>> GetByTherapistAndDateRangeAsync(int therapistId, DateOnly fromDate, DateOnly toDate);
    Task<IReadOnlyList<TherapySession>> GetByTherapistIdAndDateRangeAsync(int therapistId, DateOnly from, DateOnly to, IEnumerable<int> statusIds);
    // WP-29 (U3): one slim SQL query feeds the pending-summary/report — sessions in range that
    // still owe the therapist (ProviderAmount − SUM(allocations) > 0), no entity includes.
    Task<IReadOnlyList<OwedProviderSessionRow>> GetOwedProviderSessionRowsAsync(DateOnly from, DateOnly to, IEnumerable<int> statusIds);
    Task<IReadOnlyList<TherapySession>> GetByTreatmentPlanIdAsync(int treatmentPlanId);
    Task<IReadOnlyList<int>> GetTherapistIdsForPatientAsync(int patientId);
    Task AddRangeAsync(IEnumerable<TherapySession> sessions);
    Task<bool> HasSessionsForPlanAsync(int treatmentPlanId);
    Task<IReadOnlyList<TherapySession>> GetBySiteAndDateRangeAsync(int siteId, DateOnly from, DateOnly to);
}