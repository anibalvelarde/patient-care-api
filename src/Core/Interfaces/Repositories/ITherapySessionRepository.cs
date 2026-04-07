using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface ITherapySessionRepository : IRepository<TherapySession>
{
    Task<IReadOnlyList<TherapySession>> GetUnpaidByPatientIdsAsync(IEnumerable<int> patientIds);
    Task<IReadOnlyList<TherapySession>> GetByPatientIdsAndDateRangeAsync(IEnumerable<int> patientIds, DateOnly from, DateOnly to);
    Task<bool> HasCompletedDiscoveryAsync(int patientId);
    Task<TherapySession?> GetByIdWithSpecialtyAsync(int id);
    Task<IReadOnlyList<TherapySession>> GetCompletedDiscoverySessionsAsync(int patientId);
}