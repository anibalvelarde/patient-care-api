namespace Neurocorp.Api.Core.BusinessObjects.Patients;

/// <summary>
/// WP-55 (B-1): the canonical caretaker↔patient relationship values, mirroring the MySQL
/// <c>PatientCaretaker.RelationshipToPatient</c> enum. Before this the raw strings (notably
/// <c>"Self"</c>) were written and compared directly in <c>CaretakerProfileService</c>, and the
/// link request had NO validation at all — an unknown value truncated on insert into the enum
/// column and surfaced as a generic 500 (WP-55 bug B-2b now validates it to a 400).
///
/// <para><b>Order matters.</b> <see cref="All"/> is in DB enum-declaration order and is
/// <b>append-only</b> — enum values are stored by ordinal, so a new relationship is added at the
/// END, never inserted. The WP-55 B-4 schema-enum lockstep test parses
/// <c>patient-care-db/schema/tables/PatientCaretaker.sql</c> and asserts this list equals the DB
/// enum including order.</para>
/// </summary>
public static class CaretakerRelationships
{
    public const string Father = "Father";
    public const string Mother = "Mother";
    public const string Relative = "Relative";
    public const string HiredHelp = "HiredHelp";
    public const string LegalGuardian = "Legal-Guardian";
    public const string Self = "Self";

    /// <summary>DB enum-declaration order (append-only). Feeds the link-request validation and the lockstep test.</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { Father, Mother, Relative, HiredHelp, LegalGuardian, Self };
}
