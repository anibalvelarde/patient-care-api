namespace Neurocorp.Api.Core.BusinessObjects.Patients;

public class PatientCaretakerSummary
{
    public int CaretakerId { get; set; }
    public string CaretakerName { get; set; } = string.Empty;
    public bool IsPrimaryCaretaker { get; set; }
    public string? RelationshipToPatient { get; set; }
}
