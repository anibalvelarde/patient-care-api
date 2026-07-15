namespace Neurocorp.Api.Web.Controllers;

// WP-30: shared paging clamp (hoisted from PatientsController, WP-21). Lenient clamping
// (silent, not 400) matches the API's existing query-param style; the pageSize ceiling keeps
// "?pageSize=100000" from re-creating an unpaged endpoint.
public static class PagingParams
{
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Clamp(int page, int pageSize, int defaultPageSize) =>
        (Math.Max(1, page), pageSize < 1 ? defaultPageSize : Math.Min(pageSize, MaxPageSize));
}
