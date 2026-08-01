using System.Security.Claims;
using Neurocorp.Api.Core.Authorization;

namespace Neurocorp.Api.Web.Authorization;

/// <summary>
/// Imperative permission checks for use OUTSIDE the <c>[Authorize]</c> pipeline — e.g. response-shaping
/// filters that hide confidential fields. Mirrors <see cref="PermissionAuthorizationHandler"/> exactly:
/// a principal holds a permission if it carries the explicit (<see cref="SystemClaims.PermissionClaimType"/>,
/// value) claim OR the god-mode wildcard (<see cref="SystemClaims.SystemClaimType"/>,
/// <see cref="SystemClaims.FullAccessValue"/>).
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static bool HasPermission(this ClaimsPrincipal? user, string permission)
    {
        if (user is null)
        {
            return false;
        }

        return user.HasClaim(SystemClaims.SystemClaimType, SystemClaims.FullAccessValue)
            || user.HasClaim(SystemClaims.PermissionClaimType, permission);
    }

    /// <summary>
    /// WP-42: true for the SYSADMIN role — by role name (JWT "role" claims; RoleClaimType is
    /// configured to "role" in Startup) or by the god-mode wildcard, which the locked bootstrap
    /// design grants ONLY to that role. Backs role-gated (not claim-gated) field edits such as
    /// <c>Site.NoShowFeePct</c>.
    /// </summary>
    public static bool IsSystemAdmin(this ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return false;
        }

        return user.IsInRole(SystemClaims.SystemAdminRoleName)
            || user.HasClaim(SystemClaims.SystemClaimType, SystemClaims.FullAccessValue);
    }
}
