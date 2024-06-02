using System.Collections.Generic;

namespace Neurocorp.Api.Core.Entities;

public class TherapySession : AuditableEntityBase
{
    public TherapySession()
    {
        this.Notes = string.Empty;
    }

    public int PatientId { get; set; }
    public int TherapistId { get; set; }
    public DateTime SessionDate { get; set; }
    public decimal Duration { get; set; }
    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ProviderAmount { get; set; }
    public decimal GrossProfit  { get; set; }
    public bool IsPaidOff { get; set; }
    public string Notes { get; set; }
    public Therapist? Therapist{ get; set; }
    public Patient? Patient{ get; set; }

    public decimal AmountDue()
    {
        return Amount - DiscountAmount;
    }

    public bool GetPastDue()
    {
        return Amount > AmountPaid;
    }
}

public class UndefinedSession : TherapySession
{
    public UndefinedSession()
    {
        this.Id = -9999;
        this.PatientId = -9999;
        this.TherapistId = -9999;
        this.SessionDate = DateTime.MinValue;
        this.Notes = "Undefined Session";
    }
}