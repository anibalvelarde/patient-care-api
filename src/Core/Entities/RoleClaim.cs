namespace Neurocorp.Api.Core.Entities;

/// <summary>
/// A default claim granted to a role (role-level claim mapping). Maps to the
/// <c>RoleClaim</c> table created in migration V017. Every user assigned the role
/// inherits these claims. See <see cref="UserClaim"/> for per-user overrides.
/// </summary>
public class RoleClaim : AuditableEntityBase
{
    public int RoleId { get; set; }
    public string ClaimType { get; set; } = string.Empty;
    public string ClaimValue { get; set; } = string.Empty;
}
