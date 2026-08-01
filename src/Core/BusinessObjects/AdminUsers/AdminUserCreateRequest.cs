namespace Neurocorp.Api.Core.BusinessObjects.AdminUsers;

/// <summary>
/// WP-41B: create an operator account. The account is created ACTIVE with
/// MustChangePassword = true (G3 — temp password + forced change at next login; no mail
/// infra). Email is the login field (there is no username — WP-22 lesson).
/// </summary>
public class AdminUserCreateRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? TempPassword { get; set; }
    public List<int> OperatorRoleTypeIds { get; set; } = [];
}
