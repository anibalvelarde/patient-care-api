namespace Neurocorp.Api.Core.BusinessObjects.Statements;

public class StatementPatient
{
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public bool IsPrimaryCaretaker { get; set; }
    public string? RelationshipToPatient { get; set; }
}
