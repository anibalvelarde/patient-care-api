namespace Neurocorp.Api.Core.Interfaces.Services;

/// <summary>
/// WP-54 D8: supplies the current HTTP request's correlation id (ASP.NET's
/// <c>HttpContext.TraceIdentifier</c>) so every change written by one request shares a
/// <c>CorrelationId</c>. The Web-layer implementation reads it from the request; outside a
/// request (background/hosted work, tests) it returns <c>null</c>.
/// </summary>
public interface IRequestCorrelation
{
    string? CurrentId { get; }
}
