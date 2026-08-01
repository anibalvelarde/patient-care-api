namespace Neurocorp.Api.Core.Authorization;

/// <summary>
/// The platform's single local-password policy. Hoisted from AuthService (WP-41B) so the
/// admin temp-password flows (create operator / reset-password) enforce exactly what
/// AuthController.ChangePassword enforces — one rule, two doors.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    /// <summary>The 400-detail used whenever a supplied password is too short.</summary>
    public static string TooShortMessage(string noun) =>
        $"The {noun} must be at least {MinLength} characters long.";
}
