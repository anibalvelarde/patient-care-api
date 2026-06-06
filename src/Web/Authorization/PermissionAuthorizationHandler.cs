using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Neurocorp.Api.Core.Authorization;

namespace Neurocorp.Api.Web.Authorization;

/// <summary>
/// Grants a <see cref="PermissionRequirement"/> when the principal either holds the exact
/// permission claim or holds the god-mode wildcard ('System','FullAccess'). The wildcard
/// short-circuit is the ONLY super-power mechanism in the system (granted solely to the
/// SystemAdmin role per the locked bootstrap design).
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var user = context.User;

        var hasWildcard = user.HasClaim(SystemClaims.SystemClaimType, SystemClaims.FullAccessValue);
        var hasExplicit = user.HasClaim(requirement.ClaimType, requirement.ClaimValue);

        if (hasWildcard || hasExplicit)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
