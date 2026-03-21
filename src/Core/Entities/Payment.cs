using System.Collections.Generic;

namespace Neurocorp.Api.Core.Entities;

public class Payment
{
    public Payment()
    {
        this.Caretaker = new UndefinedCaretaker();
        this.SessionPayments = [];
    }
    public int Id { get; set; }
    public int CaretakerId { get; set; }
    public Caretaker Caretaker { get; set; }
    public int PaymentTypeId { get; set; }
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
