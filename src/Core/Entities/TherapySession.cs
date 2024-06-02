using System.Collections.Generic;

namespace Neurocorp.Api.Core.Entities;

public class TherapySession : AuditableEntityBase
{
    private const int DAYS_LATE_LIMIT = 35;

    public TherapySession()
    {
        this.Notes = string.Empty;
        this.TherapyTypes = string.Empty;
    }

    public int PatientId { get; set; }
    public int TherapistId { get; set; }
    public DateTime SessionDate { get; set; }
    public int Duration { get; set; }
    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ProviderAmount { get; set; }
    public decimal GrossProfit  { get; set; }
    public bool IsPaidOff { get; set; }
    public string Notes { get; set; }
    public string TherapyTypes { get; set; }
    public Therapist? Therapist{ get; set; }
    public Patient? Patient{ get; set; }

    public decimal AmountDue()
    {
        return Amount - DiscountAmount - AmountPaid;
    }

    public bool GetPastDue()
    {
        return (Amount > AmountPaid) && IsPastDeadline();
    }

    private bool IsPastDeadline()
    {
        var difference = DateTime.UtcNow - SessionDate;
        return difference.Days > DAYS_LATE_LIMIT;
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