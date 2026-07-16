using System.Collections.Generic;

namespace Neurocorp.Api.Core.BusinessObjects.Auth;

/// <summary>The authenticated caller's identity, roles, and effective claims.</summary>
public class CurrentUserResponse
{
    public int UserId { get; set; }
    public string? Email { get; set; }
    public string FullName { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = new List<string>();
    public IReadOnlyList<ClaimDto> Claims { get; set; } = new List<ClaimDto>();
    public bool IsSystemAdmin { get; set; }

    /// <summary>
    /// Idle auto-logoff for UI sessions, in minutes; 0 = disabled (WP-32, U4). Additive field
    /// every operator needs; sourced from the (single) Site row, 60 if no Site exists.
    /// </summary>
    public int IdleLogoffMinutes { get; set; } = 60;
}
