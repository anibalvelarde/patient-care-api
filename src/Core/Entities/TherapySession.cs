using System.Collections.Generic;

namespace Neurocorp.Api.Core.Entities;

public class TherapySession
{
    public TherapySession()
    {
        this.Notes = string.Empty;
    }

    public int Id {get; set;}
    public int PatientId {get; set;}
    public int TherapistId {get; set;}
    public DateTime SessionDate {get; set;}
    public decimal Duration {get; set;}
    public decimal DiscountAmount {get; set;}
    public string Notes {get; set;}
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