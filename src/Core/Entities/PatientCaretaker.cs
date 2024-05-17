using System.Collections.Generic;

namespace Neurocorp.Api.Core.Entities;

public class PatientCaretaker
{
    public PatientCaretaker()
    {
        this.PrimaryCaretaker = false;
        this.CreatedTimestamp = DateTime.UtcNow;
    }

    public int Id { get; set; }
    public int PatientId { get; set; }
    public int CaretakerId { get; set; }
    public bool PrimaryCaretaker { get; set; }
    public DateTime CreatedTimestamp { get; set; }
}