namespace Neurocorp.Api.Core.BusinessObjects.ServicePayments;

/// <summary>
/// A completed session that still owes the therapist some provider amount, used to
/// drive the "Pay Therapist" wizard. <c>RemainingProviderAmount</c> =
/// <c>ProviderAmount - AlreadyApplied</c> across all prior service payments.
/// </summary>
public class UnpaidProviderSessionSummary
{
    public int SessionId { get; set; }
    public string SessionDate { get; set; } = string.Empty;
    public string SessionTime { get; set; } = string.Empty;
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string TherapyType { get; set; } = string.Empty;
    public decimal ProviderAmount { get; set; }
    public decimal AlreadyApplied { get; set; }
    public decimal RemainingProviderAmount { get; set; }
}
