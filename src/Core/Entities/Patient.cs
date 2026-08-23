using System.Text;
using Neurocorp.Api.Core.Authorization;

namespace Neurocorp.Api.Core.Entities;

public class Patient : PersonBase
{
    public Patient()
    {
        this.Caretakers = [];
    }

    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? MedicalRecordNumber { get; set; }
    public string? Cedula { get; set; }
    // WP-19A: free-text remarks written by the legacy import ([LEGACY-IMPORT: ...] provenance
    // markers). Deliberately NOT surfaced in any DTO/endpoint/UI — the property exists so the
    // entity stays conformant with the DB schema (V021) for /audit-db-api.
    public string? Notes { get; set; }
    // WP-23 (F7): statutory SENADIS 20% discount — both session-create paths apply a
    // 20%-of-Amount discount floor when set.
    public bool HasSenadisDiscount { get; set; }
    // WP-37 (SEN-1): SENADIS credential expiry (V031, date NULL). NULL = no expiry (G1 —
    // open-ended; all backfill-era flags start here). Expiry only stops the booking-time
    // discount floor; the flag itself is NEVER auto-cleared (G3 — history preserved).
    public DateTime? SenadisExpirationDate { get; set; }
    // WP-24 (F3/F4): discovery-first waiver — false = exempt from the completed-discovery-
    // before-treatment rule (e.g. legacy-imported patients). Default TRUE (Questionnaire C),
    // matching the DB column default (V028).
    public bool RequiresDiscovery { get; set; } = true;
    public ICollection<PatientCaretaker> Caretakers { get; set; }

    // WP-37 (G2): THE single expiry-aware SENADIS predicate — both session-create money paths
    // (SessionEventHandler + BulkSchedulingService, via PatientProfile's delegate) route through
    // IsSenadisDiscountActive so the two floors can't drift. Expiry is compared against the
    // SESSION date, not "today": a session booked for a date on/before the expiry legitimately
    // gets the discount; boundary (session date == expiry) still floors. Expired ⇒ no floor at
    // all — but the flag is never auto-cleared (G3).
    public bool HasActiveSenadisDiscount(DateOnly sessionDate)
        => IsSenadisDiscountActive(HasSenadisDiscount, SenadisExpirationDate, sessionDate);

    public static bool IsSenadisDiscountActive(bool hasSenadisDiscount, DateTime? senadisExpirationDate, DateOnly sessionDate)
        => hasSenadisDiscount
            && (senadisExpirationDate is null
                || sessionDate <= DateOnly.FromDateTime(senadisExpirationDate.Value));

    // Retire-after-backfill (WP-36/G2): new creates always mint a permanent NC{yy}-#### MRN,
    // so this predicate (and the activation guard built on it) only matters for straggler
    // TEMP-/blank rows that predate the deploy-time backfill (0 on prod at build time).
    // Remove with the TEMP- convention cleanup WP.
    public bool HasTemporaryMrn()
    {
        return string.IsNullOrEmpty(MedicalRecordNumber)
            || MedicalRecordNumber.StartsWith("TEMP-", StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append("Pid: ").Append(this.Id).Append("  ")
        .Append("Uid: ").Append(this.User!.Id).Append("  ")
        .Append("DoB: ").Append((this.DateOfBirth ?? DateTime.MinValue).ToShortDateString()).Append("  ")
        .Append("G: ").Append(this.Gender).Append("  ")
        .Append("Mrn: ").Append(this.MedicalRecordNumber).Append("  ")
        .Append("Ced: ").Append(this.Cedula);
        return sb.ToString();
    }
    
    public UserRole MintNewRole()
    {
        return new UserRole() {
            UserId = this.User!.Id,
            RoleId = RoleTaxonomy.PatientRoleId
        };
    }
}

public class UndefinedPatient : Patient
{
    public UndefinedPatient()
    {
        this.Gender = "Undefined";
        this.User!.Id = int.MinValue;
        this.Id = int.MinValue;
        this.DateOfBirth = DateTime.MinValue;
    }
}