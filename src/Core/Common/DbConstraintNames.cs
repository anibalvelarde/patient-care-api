namespace Neurocorp.Api.Core.Common;

/// <summary>
/// WP-55 (B-1): the unique-key / column names the API matches on when translating a MySQL 1062
/// duplicate-key error into a friendly 409 message. These substrings are matched in TWO places by
/// design — <c>GlobalExceptionHandler.DuplicateKeyMessageFor</c> (the HTTP edge) and
/// <c>PatientProfileService</c>'s MRN mint-retry loop (Core, which stays infrastructure-free and
/// so can't take the Web dependency) — and they MUST agree. Centralizing the strings here is the
/// one source both point at.
///
/// <para>Note the two shapes: some are real named unique constraints (<c>uq_systemuser_email</c>,
/// <c>uq_patient_cedula</c>), while the MRN key is named after its column and the WP-50 identity
/// keys render as <c>Table.Index_NAME</c> in the driver message. Values are the exact case-insensitive
/// substrings that appear in the driver's exception text.</para>
/// </summary>
public static class DbConstraintNames
{
    public const string SystemUserEmailUnique = "uq_systemuser_email";
    public const string PatientCedulaUnique = "uq_patient_cedula";

    /// <summary>The Patient MRN unique key is named after the column itself, not <c>uq_patient_mrn</c> (B1).</summary>
    public const string PatientMrnColumn = "MedicalRecordNumber";

    public const string CaretakerUserIdUnique = "Caretaker.UserID_UNIQUE";
    public const string PatientUserIdUnique = "Patient.UserID_UNIQUE";
}
