using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Neurocorp.Api.Core.Authorization;

namespace Neurocorp.Api.Web.Authorization;

/// <summary>
/// Dynamic policy provider so controllers can require any claim with
/// <c>[Authorize(Policy = "claim:{type}:{value}")]</c> without registering each policy by hand.
/// This keeps the claims catalogue open-ended (a key 1B requirement) — new permissions need no
/// Startup change. Use <see cref="AuthPolicy"/> to build the policy names.
///
/// Policy name grammar:  claim:&lt;ClaimType&gt;:&lt;ClaimValue&gt;
/// A value with no explicit type (claim:&lt;value&gt;) defaults the type to "Permission".
/// Any other policy name falls through to the default provider.
/// </summary>
public class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    public const string PolicyPrefix = "claim:";

    private readonly DefaultAuthorizationPolicyProvider _fallbackProvider;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallbackProvider = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallbackProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallbackProvider.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PolicyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var spec = policyName.Substring(PolicyPrefix.Length);
            var separator = spec.IndexOf(':');

            string claimType;
            string claimValue;
            if (separator < 0)
            {
                claimType = SystemClaims.PermissionClaimType;
                claimValue = spec;
            }
            else
            {
                claimType = spec.Substring(0, separator);
                claimValue = spec.Substring(separator + 1);
            }

            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(claimType, claimValue))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallbackProvider.GetPolicyAsync(policyName);
    }
}
