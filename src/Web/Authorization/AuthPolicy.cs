using Neurocorp.Api.Core.Authorization;

namespace Neurocorp.Api.Web.Authorization;

/// <summary>
/// Builds policy names understood by <see cref="PermissionPolicyProvider"/>.
///
/// On an <c>[Authorize]</c> attribute the policy must be a compile-time constant, so use the
/// <see cref="PermissionPrefix"/> const concatenated with a generated <c>Permissions</c> claim:
/// <c>[Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AdminSitesManage)]</c>.
/// The <see cref="Permission(string)"/> method is for runtime policy-name construction.
/// </summary>
public static class AuthPolicy
{
    /// <summary>
    /// Constant policy-name prefix for a Permission claim (<c>"claim:Permission:"</c>), suitable for
    /// use in attributes. Append a claim value to form a full policy name.
    /// </summary>
    public const string PermissionPrefix =
        PermissionPolicyProvider.PolicyPrefix + SystemClaims.PermissionClaimType + ":";

    public static string Permission(string value) =>
        $"{PermissionPolicyProvider.PolicyPrefix}{SystemClaims.PermissionClaimType}:{value}";

    public static string Claim(string type, string value) =>
        $"{PermissionPolicyProvider.PolicyPrefix}{type}:{value}";
}
