namespace Neurocorp.Api.Core.Authorization;

/// <summary>
/// WP-41 (G1/G4): partitions RoleType rows into IDENTITY roles (Patient / Therapist /
/// Caretaker — workflow-created, clinical/billing subjects, System/Restricted, never
/// managed by the admin-users API) and OPERATOR roles (everything else: SystemAdmin,
/// Manager, AssistantManager, FrontDesk, Owner, Accountant, and any future non-identity
/// role). Per the G4 ruling roles are purely additive — both kinds may coexist on one
/// SystemUser; the admin-users API touches only the operator links.
/// </summary>
public static class RoleTaxonomy
{
    /// <summary>
    /// RoleType.RoleName of the privileged SA role (abbreviation SYSADMIN — bootstrap-sa.sql's
    /// canonical name). Backs the G5 self-demote / last-SYSADMIN guard rails.
    /// (WP-42B independently introduces SystemClaims.SystemAdminRoleName for its Site-fee role
    /// gate; if both land, consolidating the two constants is a trivial follow-up.)
    /// </summary>
    public const string SystemAdminRoleName = "SystemAdmin";

    /// <summary>RoleType.RoleName values of the three identity roles (V017 seed).</summary>
    public static readonly IReadOnlyList<string> IdentityRoleNames =
        new[] { "Patient", "Therapist", "Caretaker" };

    public static bool IsIdentityRole(string roleName) =>
        IdentityRoleNames.Contains(roleName, StringComparer.OrdinalIgnoreCase);
}
