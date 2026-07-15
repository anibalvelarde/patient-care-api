namespace Neurocorp.Api.Core.BusinessObjects.Patients;

// WP-30 (U2): slim typeahead row for dropdown/picker consumers — never the full census.
// Deliberately include-free: name + MRN is all any audited picker displays (gate G3).
public class PatientLookupItem
{
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? MedicalRecordNumber { get; set; }
}
