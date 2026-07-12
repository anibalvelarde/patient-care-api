namespace Neurocorp.Api.Core.Exceptions;

/// <summary>
/// Thrown by the domain/service layer when a request is well-formed and the resources exist,
/// but domain state forbids the operation (e.g. merging a patient whose SystemUser holds
/// non-Patient roles). The global exception handler maps this to a 409 ProblemDetails —
/// the domain-driven sibling of the DbUpdateException/1062 duplicate-key 409.
/// </summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}
