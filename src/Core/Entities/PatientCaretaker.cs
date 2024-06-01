using System.Collections.Generic;

namespace Neurocorp.Api.Core.Entities;

public class PatientCaretaker : AuditableEntityBase
{
    public PatientCaretaker()
    {
        this.PrimaryCaretaker = false;
        this.CreatedTimestamp = DateTime.UtcNow;
    }

    public int PatientId { get; set; }
    public Patient? Patient{ get; set; }
    public int CaretakerId { get; set; }
    public Caretaker? Caretaker{ get; set; }
    public bool PrimaryCaretaker { get; set; }
}