using System.Collections.Generic;

namespace Neurocorp.Api.Core.Entities;

public class Therapist
{
    public Therapist()
    {
        this.Name = "";
        this.Specialties = Enumerable.Empty<string>();
    }

    public int Id {get; set;}
    public string Name {get; set;}
    public IEnumerable<string> Specialties {get; set;}
}