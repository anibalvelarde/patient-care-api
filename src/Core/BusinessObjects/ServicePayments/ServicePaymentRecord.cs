using System.Collections.Generic;
using Neurocorp.Api.Core.BusinessObjects.Payments;

namespace Neurocorp.Api.Core.BusinessObjects.ServicePayments;

/// <summary>Response shape for a service payment (therapist disbursement) and its allocations.</summary>
public class ServicePaymentRecord
{
    public int ServicePaymentId { get; set; }
    public int TherapistId { get; set; }
    public string TherapistName { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentTypeInfo PaymentType { get; set; } = new();
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public List<ServiceAllocationDetail> Allocations { get; set; } = new();
    public decimal TotalApplied { get; set; }
    public decimal UnallocatedAmount { get; set; }

    /// <summary>
    /// If set, this record is itself a reversal entry (WP-14.5) — an offsetting negative
    /// disbursement that reverses the referenced original payment. Null for normal payments.
    /// </summary>
    public int? ReversesServicePaymentId { get; set; }

    /// <summary>
    /// True when this payment has been reversed by a later reversal entry. Originals that are
    /// already reversed (or are themselves reversals) cannot be reversed again.
    /// </summary>
    public bool IsReversed { get; set; }
}
