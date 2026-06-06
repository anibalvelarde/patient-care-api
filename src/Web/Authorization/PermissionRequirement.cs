using Microsoft.AspNetCore.Authorization;

namespace Neurocorp.Api.Web.Authorization;

/// <summary>
/// An authorization requirement satisfied by a single (ClaimType, ClaimValue) pair —
/// or by the ('System','FullAccess') wildcard. See <see cref="PermissionAuthorizationHandler"/>.
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public string ClaimType { get; }
    public string ClaimValue { get; }

    public PermissionRequirement(string claimType, string claimValue)
    {
        ClaimType = claimType;
        ClaimValue = claimValue;
    }
}
