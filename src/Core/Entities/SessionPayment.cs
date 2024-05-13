using System.Collections.Generic;

namespace Neurocorp.Api.Core.Entities;

public class SessionPayment
{
    public SessionPayment()
    {
        this.Payment = new UndefinedPayment();
        this.TherapySession = new UndefinedSession();
    }

    public int Id { get; set; }
    public int PaymentId { get; set; }
    public Payment Payment { get; set; }
    public int TherapySessionId { get; set; }
    public TherapySession TherapySession { get; set; }
}
