namespace Neurocorp.Api.Core.BusinessObjects.ServicePayments;

/// <summary>
/// One service-payment allocation that covered (part of) a session in the requested range,
/// so the UI can show <i>which</i> payment already settled it (WP-20). Reversal allocations
/// appear like any other (negative <c>AmountApplied</c>) — they net into the session's total.
/// </summary>
public class PaidByPaymentRef
{
    public int ServicePaymentId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string PaymentDate { get; set; } = string.Empty;
    public decimal AmountApplied { get; set; }
}
