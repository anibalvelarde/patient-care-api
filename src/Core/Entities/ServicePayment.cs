using System.Collections.Generic;

namespace Neurocorp.Api.Core.Entities;

/// <summary>
/// A disbursement from the clinic to a therapist (therapist payroll). The
/// direction-flipped mirror of <see cref="Payment"/>: the clinic pays a
/// therapist instead of receiving from a caretaker. Reuses the
/// <see cref="PaymentType"/> lookup. Allocated across sessions via
/// <see cref="SessionServicePayment"/>.
/// </summary>
public class ServicePayment : AuditableEntityBase
{
    public ServicePayment()
    {
        this.Therapist = null!;
        this.SessionServicePayments = [];
    }

    public int TherapistId { get; set; }
    public Therapist Therapist { get; set; }
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public int PaymentTypeId { get; set; }
    public PaymentType? PaymentType { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Self-reference reserved for WP-14.5 append-only reversals/corrections.
    /// Always null in the WP-14 MVP. Modeled as a bare scalar (no navigation)
    /// on purpose so EF does not infer a self-referencing relationship.
    /// </summary>
    public int? ReversesServicePaymentId { get; set; }

    public ICollection<SessionServicePayment> SessionServicePayments { get; set; }
}
