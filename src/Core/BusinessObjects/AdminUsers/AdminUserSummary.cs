namespace Neurocorp.Api.Core.BusinessObjects.AdminUsers;

/// <summary>
/// WP-41B: one operator role on an admin-users summary (the manageable set).
/// Wire shape per _contracts/admin-users-api.md: { roleTypeId, name }.
/// </summary>
public class AdminUserRoleInfo
{
    public int RoleTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// WP-41B: admin user-management projection of a SystemUser holding at least one operator
/// role (G1 scope). <see cref="OperatorRoles"/> is the manageable set; <see cref="IdentityRoles"/>
/// is informational only ("also a Patient" hints) and is never editable through this API (G4).
/// </summary>
public class AdminUserSummary
{
    public int UserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool MustChangePassword { get; set; }
    public IReadOnlyList<AdminUserRoleInfo> OperatorRoles { get; set; } = [];
    public IReadOnlyList<string> IdentityRoles { get; set; } = [];
}
