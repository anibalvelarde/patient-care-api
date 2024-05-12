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