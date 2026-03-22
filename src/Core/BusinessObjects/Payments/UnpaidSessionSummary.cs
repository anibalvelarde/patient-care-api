namespace Neurocorp.Api.Core.BusinessObjects.Payments;

public class UnpaidSessionSummary
{
    public UnpaidSessionSummary()
    {
        PatientName = string.Empty;
        TherapistName = string.Empty;
    }

    public int SessionId { get; set; }
    public string SessionDate { get; set; } = string.Empty;
    public string SessionTime { get; set; } = string.Empty;
    public int PatientId { get; set; }
    public string PatientName { get; set; }
    public string TherapistName { get; set; }
    public decimal Amount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountDue { get; set; }
    public bool IsPastDue { get; set; }
}
