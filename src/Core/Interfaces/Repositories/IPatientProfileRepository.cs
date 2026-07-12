using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Patients;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface IPatientProfileRepository : IRepository<PatientProfile>
{
    public Task<PatientProfile> UpdateAsync(int patientId, int userId, PatientProfileUpdateRequest updateRequest);
    // WP-21 (F1): paged patient summaries ordered most-recent-session first; optional
    // case-insensitive search on first/last name and MRN.
    public Task<PagedResult<PatientSessionHistorySummary>> GetSessionHistoryAsync(string? search, int page, int pageSize);
}