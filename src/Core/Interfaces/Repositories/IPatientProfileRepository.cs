using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Patients;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface IPatientProfileRepository : IRepository<PatientProfile>
{
    public Task<PatientProfile> UpdateAsync(int patientId, int userId, PatientProfileUpdateRequest updateRequest);
    // WP-29 (U3): batched profile fetch — one query for N ids instead of N GetByIdAsync calls.
    public Task<IReadOnlyList<PatientProfile>> GetByIdsAsync(IReadOnlyCollection<int> patientIds);
    // WP-21 (F1): paged patient summaries ordered most-recent-session first; optional
    // case-insensitive search on first/last name and MRN.
    // WP-35 addendum: SessionHistoryPagedResult = PagedResult<PatientSessionHistorySummary>
    // + envelope-level Totals over the full filtered set.
    public Task<SessionHistoryPagedResult> GetSessionHistoryAsync(string? search, int page, int pageSize);
    // WP-30 (U2): paged main list — search over name/MRN/cedula, optional isActive filter,
    // ordered by name (tiebreak id). Count runs include-free; the page hydrates via GetByIdsAsync.
    public Task<PagedResult<PatientProfile>> GetPagedAsync(string? search, bool? isActive, int page, int pageSize);
    // WP-30 (U2): typeahead — same search fields, capped, slim include-free projection.
    public Task<IReadOnlyList<PatientLookupItem>> LookupAsync(string query, int maxResults);
}