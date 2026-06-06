namespace Neurocorp.Api.Core.Entities;

/// <summary>
/// A per-user claim grant or explicit denial. Maps to the <c>UserClaim</c> table
/// created in migration V017. When <see cref="IsDenied"/> is true the row removes a
/// claim the user would otherwise inherit from a role; otherwise it grants an extra claim.
/// </summary>
public class UserClaim : AuditableEntityBase
{
    public int UserId { get; set; }
    public string ClaimType { get; set; } = string.Empty;
    public string ClaimValue { get; set; } = string.Empty;
    public bool IsDenied { get; set; }
}
