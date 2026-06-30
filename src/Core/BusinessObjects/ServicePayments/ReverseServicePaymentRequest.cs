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

    /// <summary>
    /// The business date to stamp on the reversal entry, as the user's local calendar day. The UI
    /// sends "today" in the browser's timezone so the reversal lines up with the other (date-only)
    /// payments in the history. When omitted, the server falls back to today's UTC date. The precise
    /// reversal instant is always preserved separately in the audit <c>CreatedTimestamp</c>.
    /// </summary>
    public DateTime? PaymentDate { get; set; }
}
