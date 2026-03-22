namespace Neurocorp.Api.Core.BusinessObjects.Payments;

public class AllocationDetail
{
    public int SessionPaymentId { get; set; }
    public int SessionId { get; set; }
    public string SessionDate { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string TherapistName { get; set; } = string.Empty;
    public decimal AmountAllocated { get; set; }
}
