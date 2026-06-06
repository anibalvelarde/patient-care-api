namespace Neurocorp.Api.Core.Authorization;

/// <summary>
/// Well-known authorization claim constants shared across layers.
///
/// The platform is claims-aware: a permission is a (<see cref="PermissionClaimType"/>, value)
/// pair such as ("Permission", "Patients.FullAccess"). The single god-mode claim
/// (<see cref="SystemClaimType"/> = <see cref="FullAccessValue"/>) acts as a wildcard that
/// satisfies every policy. Per the locked design it is granted ONLY to the SystemAdmin role.
/// See remediation-2026-system-admin-bootstrap.md.
/// </summary>
public static class SystemClaims
{
    /// <summary>Claim type for the god-mode wildcard.</summary>
    public const string SystemClaimType = "System";

    /// <summary>Claim value that grants unrestricted access.</summary>
    public const string FullAccessValue = "FullAccess";

    /// <summary>Conventional claim type for granular permissions.</summary>
    public const string PermissionClaimType = "Permission";

    /// <summary>Custom JWT claim carrying the authenticated user's SystemUser.UserID.</summary>
    public const string UserIdClaimType = "uid";

    /// <summary>
    /// Custom JWT claim present (value "true") when the user still owes a password change.
    /// While set, <c>PasswordChangeRequiredMiddleware</c> blocks all but the change-password flow.
    /// The claim disappears automatically once the password is changed (tokens are re-issued).
    /// </summary>
    public const string MustChangePasswordClaimType = "mcp";

    /// <summary>Value emitted for <see cref="MustChangePasswordClaimType"/> when a change is owed.</summary>
    public const string MustChangePasswordValue = "true";
}
