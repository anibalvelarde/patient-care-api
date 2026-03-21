namespace Neurocorp.Api.Core.BusinessObjects.Patients;

public class CaretakerPatientSummary
{
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public bool IsPrimaryCaretaker { get; set; }
    public string? RelationshipToPatient { get; set; }
}
