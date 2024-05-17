using System.Collections.Generic;

namespace Neurocorp.Api.Core.Entities;

public class Payment
{
    public Payment()
    {
        this.Patient = new UndefinedPatient();
        this.SessionPayments = [];
    }
    public int Id { get; set; }
    public int PatientId { get; set; }
    public Patient Patient { get; set; }
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