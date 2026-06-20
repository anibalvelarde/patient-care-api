using System.Collections.Generic;

namespace Neurocorp.Api.Core.BusinessObjects.ServicePayments;

/// <summary>
/// Result of a "Run Payroll" batch: the payments actually created (one per therapist that had
/// something owed), plus run totals. Therapists in the request with nothing owed at run time are
/// skipped and simply do not appear in <see cref="Payments"/>.
/// </summary>
public class BatchPayrollResult
{
    public List<ServicePaymentRecord> Payments { get; set; } = new();
    public int TherapistCount { get; set; }
    public decimal TotalPaid { get; set; }
}
