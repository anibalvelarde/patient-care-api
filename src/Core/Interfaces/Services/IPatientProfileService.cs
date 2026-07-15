using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Patients;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface IPatientProfileService : IService<PatientProfile>
{
    // other business logic operations...
    public Task<PatientProfile> CreateAsync(PatientProfileRequest request);
    public Task<bool> UpdateAsync(int patientId, PatientProfileUpdateRequest request);
    public Task<bool> VerifyRequestAsync(int patientAggId);
    // WP-29 (U3): batched GetByIdAsync — one profile query + one discovery-status query for N
    // ids, instead of 2 round trips per patient.
    public Task<IReadOnlyList<PatientProfile>> GetByIdsAsync(IReadOnlyCollection<int> patientIds);
    Task<IEnumerable<PatientCaretakerSummary>> GetCaretakersForPatientAsync(int patientId);
    // WP-21 (F1): backs the Patients → Session History tab's paged patient list.
    Task<PagedResult<PatientSessionHistorySummary>> GetSessionHistoryAsync(string? search, int page, int pageSize);
}
