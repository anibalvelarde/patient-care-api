using System.Collections.Generic;

namespace Neurocorp.Api.Core.Entities;

public class Therapist
{
    public Therapist()
    {
        // this.Specialties = [];
    }

    public int TherapistId { get; set; }
    public int UserId { get; set; }
    public decimal FeePerSession { get; set; }
    public decimal FeePctPerSession { get; set; }
//    public IEnumerable<string> Specialties { get; set; }
    public User? User { get; set; }
}

public class UndefinedTherapist : Therapist
{
    public UndefinedTherapist()
    {
        // this.Specialties = [];
    }
}