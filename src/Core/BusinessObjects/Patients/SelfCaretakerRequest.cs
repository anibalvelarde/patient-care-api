namespace Neurocorp.Api.Core.BusinessObjects.Patients;

/// <summary>
/// WP-50B: request to make an existing patient their own caretaker. Minimal input by design —
/// the operation attaches a Caretaker role to the patient's EXISTING SystemUser (never mints a
/// second user, so no email is required and the unique-email login identity is untouched) and
/// creates the self-link (RelationshipToPatient = "Self").
/// </summary>
public class SelfCaretakerRequest
{
    public int PatientId { get; set; }
    public bool IsPrimary { get; set; } = true;
}
