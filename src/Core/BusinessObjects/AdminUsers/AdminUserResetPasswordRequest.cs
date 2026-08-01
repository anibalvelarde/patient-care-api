namespace Neurocorp.Api.Core.BusinessObjects.AdminUsers;

/// <summary>
/// WP-41B: admin-set temporary password (G3). Sets the hash + MustChangePassword = true so the
/// user's next login is forced through AuthController.ChangePassword (existing "mcp" flow).
/// </summary>
public class AdminUserResetPasswordRequest
{
    public string? TempPassword { get; set; }
}
