namespace Neurocorp.Api.Core.BusinessObjects.ServicePayments;

/// <summary>One session allocation as rendered on a service-payment record.</summary>
public class ServiceAllocationDetail
{
    public int SessionServicePaymentId { get; set; }
    public int SessionId { get; set; }
    public string SessionDate { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public decimal AmountApplied { get; set; }
}
