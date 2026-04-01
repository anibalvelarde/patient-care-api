namespace Neurocorp.Api.Core.Entities;

public class TherapistSpecialty : AuditableEntityBase
{
    public int TherapistSpecialtyId { get; set; }
    public int TherapistId { get; set; }
    public int SpecialtyId { get; set; }
    public Therapist? Therapist { get; set; }
    public SpecialtyType? SpecialtyType { get; set; }
}
