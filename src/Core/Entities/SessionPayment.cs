using System.Collections.Generic;

namespace Neurocorp.Api.Core.Entities;

public class SessionPayment : AuditableEntityBase
{
    public SessionPayment()
    {
        this.Payment = null!;
        this.TherapySession = null!;
    }

    public int PaymentId { get; set; }
    public Payment Payment { get; set; }
    public int TherapySessionId { get; set; }
    public TherapySession TherapySession { get; set; }
    public decimal AmountAllocated { get; set; }
}
