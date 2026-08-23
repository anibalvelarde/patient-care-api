namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

/// <summary>
/// WP-55 (B-1): canonical appointment-confirmation values, mirroring the MySQL
/// <c>AppointmentConfirmation</c> enums (<c>ConfirmationResult</c> and <c>ConfirmationMethod</c>).
/// Before this the result strings were compared inline in <c>BookingService</c> and the method
/// default <c>"Phone"</c> was duplicated on both the request DTO and the entity, and the incoming
/// <c>ConfirmationResult</c> was unvalidated — an unknown value truncated into the enum column and
/// caused a 500 (WP-55 bug B-2e now validates it to a 400).
///
/// <para><see cref="AllResults"/> / <see cref="AllMethods"/> are in DB enum-declaration order
/// (append-only); the WP-55 B-4 lockstep test asserts <c>AllResults</c> equals
/// <c>patient-care-db/schema/tables/AppointmentConfirmation.sql</c>'s <c>ConfirmationResult</c> enum.</para>
/// </summary>
public static class ConfirmationValues
{
    // --- Results (AppointmentConfirmation.ConfirmationResult) -----------------------------------
    public const string Confirmed = "Confirmed";
    public const string NoAnswer = "NoAnswer";
    public const string LeftMessage = "LeftMessage";
    public const string Declined = "Declined";

    public static readonly IReadOnlyList<string> AllResults =
        new[] { Confirmed, NoAnswer, LeftMessage, Declined };

    // --- Methods (AppointmentConfirmation.ConfirmationMethod); Phone is the default -------------
    public const string MethodPhone = "Phone";
    public const string MethodEmail = "Email";
    public const string MethodText = "Text";
    public const string MethodInPerson = "InPerson";

    public static readonly IReadOnlyList<string> AllMethods =
        new[] { MethodPhone, MethodEmail, MethodText, MethodInPerson };
}
