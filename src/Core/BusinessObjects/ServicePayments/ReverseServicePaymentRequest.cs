namespace Neurocorp.Api.Core.BusinessObjects.ServicePayments;

/// <summary>
/// Payload for reversing a service payment (WP-14.5). Append-only: the reversal writes an
/// offsetting negative <see cref="Entities.ServicePayment"/> linked to the original via
/// <c>ReversesServicePaymentID</c>; the original is never mutated or deleted. A reason is
/// required and is recorded in the reversal entry's Notes for audit.
/// </summary>
public class ReverseServicePaymentRequest
{
    public string Reason { get; set; } = string.Empty;
}
