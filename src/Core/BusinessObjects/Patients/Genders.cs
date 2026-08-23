namespace Neurocorp.Api.Core.BusinessObjects.Patients;

/// <summary>
/// WP-55 (B-1): canonical patient gender values, mirroring the MySQL <c>Patient.Gender</c> enum.
/// The allowed set was duplicated as string literals in two <c>[AllowedValues]</c> attributes
/// (<c>PatientProfileRequest</c>, <c>PatientProfileUpdateRequest</c>); those now reference these
/// consts (attribute args must be compile-time constants — hence <c>const string</c>, not the array).
///
/// <para>The update DTO deliberately also allows <c>null</c> and <c>""</c> as the "leave unchanged /
/// clear" update-delta — that stays as literals in the attribute, it is not a gender value.
/// <c>"Undefined"</c> is a null-object display sentinel only and is never persisted, so it is not
/// part of <see cref="All"/> or the DB enum.</para>
///
/// <para><see cref="All"/> is in DB enum-declaration order (append-only); the WP-55 B-4 lockstep
/// test asserts it equals <c>patient-care-db/schema/tables/Patient.sql</c>'s <c>Gender</c> enum.</para>
/// </summary>
public static class Genders
{
    public const string Male = "Male";
    public const string Female = "Female";
    public const string Other = "Other";

    /// <summary>DB enum-declaration order (append-only).</summary>
    public static readonly IReadOnlyList<string> All = new[] { Male, Female, Other };
}
