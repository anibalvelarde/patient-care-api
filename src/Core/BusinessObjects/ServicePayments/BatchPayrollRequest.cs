using System.Collections.Generic;

namespace Neurocorp.Api.Core.BusinessObjects.ServicePayments;

/// <summary>
/// "Run Payroll" batch: pay the full remaining provider amount for every completed unpaid session in
/// [<see cref="From"/>, <see cref="To"/>] for each therapist in <see cref="TherapistIds"/>. A single
/// shared payment method + date apply to the whole run (per-therapist exceptions go through the
/// single-therapist wizard). Produces one ServicePayment per therapist that has anything owed.
/// </summary>
public class BatchPayrollRequest
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public DateTime PaymentDate { get; set; }
    public int PaymentTypeId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public List<int> TherapistIds { get; set; } = new();
}
