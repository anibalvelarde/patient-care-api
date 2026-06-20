namespace Neurocorp.Api.Core.Entities;

/// <summary>
/// Junction that allocates a <see cref="ServicePayment"/> across the therapy
/// sessions it pays for. The direction-flipped mirror of <see cref="SessionPayment"/>.
/// </summary>
public class SessionServicePayment : AuditableEntityBase
{
    public SessionServicePayment()
    {
        this.ServicePayment = null!;
        this.TherapySession = null!;
    }

    public int ServicePaymentId { get; set; }
    public ServicePayment ServicePayment { get; set; }
    public int TherapySessionId { get; set; }
    public TherapySession TherapySession { get; set; }
    public decimal AmountApplied { get; set; }
}
