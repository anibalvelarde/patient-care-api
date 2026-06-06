using Neurocorp.Api.Core.Authorization;

namespace Neurocorp.Api.Web.Authorization;

/// <summary>
/// Builds policy names understood by <see cref="PermissionPolicyProvider"/>.
/// Usage: <c>[Authorize(Policy = AuthPolicy.Permission("Patients.FullAccess"))]</c>.
/// </summary>
public static class AuthPolicy
{
    public static string Permission(string value) =>
        $"{PermissionPolicyProvider.PolicyPrefix}{SystemClaims.PermissionClaimType}:{value}";

    public static string Claim(string type, string value) =>
        $"{PermissionPolicyProvider.PolicyPrefix}{type}:{value}";
}
