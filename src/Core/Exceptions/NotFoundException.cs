namespace Neurocorp.Api.Core.Exceptions;

/// <summary>
/// Thrown by the domain/service layer when a requested resource does not exist. The global
/// exception handler maps this to a 404 ProblemDetails. This is the sanctioned "not found"
/// signal — prefer it over returning null + a controller-level NotFound() so error shaping
/// stays centralized.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }

    public NotFoundException(string resource, object key)
        : base($"{resource} '{key}' was not found.") { }
}
