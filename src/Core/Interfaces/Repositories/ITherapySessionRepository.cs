using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface ITherapySessionRepository : IRepository<TherapySession>
{
    Task<IReadOnlyList<TherapySession>> GetUnpaidByPatientIdsAsync(IEnumerable<int> patientIds);
    Task<IReadOnlyList<TherapySession>> GetByPatientIdsAndDateRangeAsync(IEnumerable<int> patientIds, DateOnly from, DateOnly to);
    Task<bool> HasCompletedDiscoveryAsync(int patientId);
    Task<TherapySession?> GetByIdWithSpecialtyAsync(int id);
    Task<IReadOnlyList<TherapySession>> GetCompletedDiscoverySessionsAsync(int patientId);
    Task<IReadOnlyList<TherapySession>> GetByTherapistAndDateRangeAsync(int therapistId, DateOnly fromDate, DateOnly toDate);
    Task<IReadOnlyList<TherapySession>> GetByTreatmentPlanIdAsync(int treatmentPlanId);
    Task<IReadOnlyList<int>> GetTherapistIdsForPatientAsync(int patientId);
    Task AddRangeAsync(IEnumerable<TherapySession> sessions);
    Task<bool> HasSessionsForPlanAsync(int treatmentPlanId);
}