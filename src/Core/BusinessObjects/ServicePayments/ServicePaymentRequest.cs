using System.Collections.Generic;

namespace Neurocorp.Api.Core.BusinessObjects.ServicePayments;

/// <summary>Create payload for issuing a service payment (therapist disbursement).</summary>
public class ServicePaymentRequest
{
    public int TherapistId { get; set; }
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public int PaymentTypeId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public List<SessionServiceAllocationItem> SessionAllocations { get; set; } = new();
}
