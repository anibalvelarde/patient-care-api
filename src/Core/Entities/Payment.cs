using System.Collections.Generic;

namespace Neurocorp.Api.Core.Entities;

public class Payment : AuditableEntityBase
{
    public Payment()
    {
        this.Caretaker = null!;
        this.SessionPayments = [];
    }
    public int CaretakerId { get; set; }
    public Caretaker Caretaker { get; set; }
    public int PaymentTypeId { get; set; }
    public PaymentType? PaymentType { get; set; }
    public string? CheckNumber { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public ICollection<SessionPayment> SessionPayments { get; set; }
}

public class UndefinedPayment : Payment
{
    public UndefinedPayment()
    {
        this.SessionPayments = [];
    }
}
