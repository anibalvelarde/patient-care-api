using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Patients;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface ICaretakerProfileRepository : IRepository<CaretakerProfile>
{
    public Task<CaretakerProfile> UpdateAsync(int caretakerId, int userId, CaretakerProfileUpdateRequest updateRequest);
    // WP-30 (U2): paged main list — search over name/email, optional isActive filter,
    // ordered by name (tiebreak id). Count runs include-free.
    public Task<PagedResult<CaretakerProfile>> GetPagedAsync(string? search, bool? isActive, int page, int pageSize);
    // WP-30 (U2): typeahead — same search fields, capped, slim include-free projection.
    public Task<IReadOnlyList<CaretakerLookupItem>> LookupAsync(string query, int maxResults);
}