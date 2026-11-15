namespace Neurocorp.Api.Core.BusinessObjects.Patients;

// WP-62: body of PUT /api/caretakers/{id}/patients/{patientId} — only the primary flag is editable
// on an existing link (relationship changes stay unlink + relink).
public class PatientLinkUpdateRequest
{
    public bool IsPrimary { get; set; }
}
