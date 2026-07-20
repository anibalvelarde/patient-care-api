using Neurocorp.Api.Core.BusinessObjects.Common;

namespace Neurocorp.Api.Core.BusinessObjects.Patients;

public class PatientProfile : IProfile, IHasAudit
{
    public PatientProfile()
    {
        this.PatientName = string.Empty;
    }

    public int PatientId { get; set; }
    public int UserId { get; set; }
    public string PatientName { get; set; }
    public string? MedicalRecordNumber { get; set; }
    public string? Cedula { get; set; }
    public DateTime DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime CreatedTimestamp { get; set; }
    public string? Gender { get; set; }
    public bool IsActive { get; set; }
    // WP-23 (F7): statutory SENADIS 20% discount flag; edit gated by Patients.SenadisDiscount.Edit.
    public bool HasSenadisDiscount { get; set; }
    // WP-37 (SEN-1): SENADIS credential expiry. NULL = no expiry (G1). Rides the SAME edit
    // gate as the flag (G4 — one claim governs both SENADIS fields).
    public DateTime? SenadisExpirationDate { get; set; }

    // WP-37 (G2): delegates to the ONE shared predicate on the Patient entity — both
    // session-create money paths call this so the floors can't drift.
    public bool HasActiveSenadisDiscount(DateOnly sessionDate)
        => Entities.Patient.IsSenadisDiscountActive(HasSenadisDiscount, SenadisExpirationDate, sessionDate);
    // WP-24 (F3/F4): discovery-first waiver; false = exempt. Edit gated by
    // Patients.RequiresDiscovery.Edit.
    public bool RequiresDiscovery { get; set; }
    public bool? HasCompletedDiscovery { get; set; }
    // WP-31 (U1): additive audit block for the row ⓘ popover.
    public AuditInfo? Audit { get; set; }
    public List<PatientCaretakerSummary> Caretakers { get; set; } = new();

    int IProfile.Id => this.PatientId;
    string IProfile.Name => this.PatientName;
    bool IProfile.IsValid => true;
}

public class UndefinedPatientProfile : PatientProfile
{
    public UndefinedPatientProfile()
    {
        this.PatientName = "UNDEFINED";
    }
}