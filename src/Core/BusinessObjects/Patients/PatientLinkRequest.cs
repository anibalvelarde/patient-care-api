namespace Neurocorp.Api.Core.BusinessObjects.Patients;

public class PatientLinkRequest
{
    public int PatientId { get; set; }
    public bool IsPrimary { get; set; }
    public string? Relationship { get; set; }
}
