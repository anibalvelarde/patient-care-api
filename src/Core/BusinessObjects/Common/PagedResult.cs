namespace Neurocorp.Api.Core.BusinessObjects.Common;

/// <summary>
/// Shared pagination envelope — <c>{ items, page, pageSize, totalCount }</c> (WP-21, the
/// platform's first paged-response convention; reuse for future paged endpoints).
/// <see cref="Page"/> is 1-based; <see cref="TotalCount"/> is the size of the full filtered set,
/// not the page. <see cref="Items"/> is deliberately settable so response filters
/// (e.g. ProviderAmountResultFilter) can swap in a materialized, shaped list.
/// </summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}
