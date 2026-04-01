namespace Neurocorp.Api.Core.BusinessObjects.Therapists;

public class TherapistSpecialtyItem
{
    public int SpecialtyId { get; set; }
    public string Abbreviation { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
