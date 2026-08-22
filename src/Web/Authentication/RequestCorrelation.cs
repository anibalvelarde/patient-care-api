using Microsoft.AspNetCore.Http;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Web.Authentication;

/// <summary>
/// WP-54 D8: the current request's correlation id (<c>HttpContext.TraceIdentifier</c>), so every
/// change written by one HTTP request shares a <c>CorrelationId</c>. Null outside a request
/// (background/hosted work, tests) — the change-log row's CorrelationId is then null.
/// </summary>
public class RequestCorrelation : IRequestCorrelation
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RequestCorrelation(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? CurrentId => _httpContextAccessor.HttpContext?.TraceIdentifier;
}
