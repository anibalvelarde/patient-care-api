using System.Collections.Generic;

namespace Neurocorp.Api.Core.BusinessObjects.ServicePayments;

/// <summary>
/// A completed session in the requested range that already has service-payment money applied
/// (fully or partially paid), with the payment reference(s) that covered it (WP-20).
/// <c>RemainingProviderAmount</c> is 0 when fully paid; when &gt; 0 the session is partially
/// paid and also appears in the payable list.
/// </summary>
public class PaidProviderSessionDetail
{
    public int SessionId { get; set; }
    public string SessionDate { get; set; } = string.Empty;
    public string SessionTime { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string TherapyType { get; set; } = string.Empty;
    public decimal ProviderAmount { get; set; }
    public decimal AmountApplied { get; set; }
    public decimal RemainingProviderAmount { get; set; }
    public List<PaidByPaymentRef> PaymentReferences { get; set; } = new();
}
