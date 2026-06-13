using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Neurocorp.Api.Core.Authorization;
using Neurocorp.Api.Core.BusinessObjects.Auth;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Web;

namespace Neurocorp.Api.Web.Tests.Authorization;

/// <summary>
/// WP-17B-2 integration-test host. Boots the REAL request pipeline — JWT bearer validation,
/// the dynamic <c>PermissionPolicyProvider</c>, and <c>PermissionAuthorizationHandler</c> — so a
/// 200/403 result genuinely exercises the access-control wiring, not a stub.
///
/// Two deliberate swaps keep it hermetic:
///   * the MySQL <see cref="ApplicationDbContext"/> is replaced with EF InMemory, so no database
///     is required and a granted request reaches the controller without hitting MySQL;
///   * a fixed test JWT signing key is injected via config.
///
/// Tokens are minted through the app's own <see cref="ITokenService"/>, so they are signed and
/// shaped exactly like production tokens — the test can't drift from how the API issues/validates.
/// A role token carries precisely the Permission claims the access-control manifest grants that
/// role (i.e. what the WP-17A DB seed produces at login); the wildcard token carries the single
/// ('System','FullAccess') god claim.
/// </summary>
public class AccessControlTestFactory : WebApplicationFactory<Program>
{
    private const string TestSigningKey =
        "TEST-ONLY-JWT-SIGNING-KEY-FOR-ACCESS-CONTROL-INTEGRATION-TESTS-0123456789";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = TestSigningKey,
                ["Jwt:Issuer"] = "neurocorp-api",
                ["Jwt:Audience"] = "neurocorp-clients",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Drop the MySQL DbContext wiring and replace it with an in-memory store so the host
            // boots without a database and granted requests can reach the controller.
            var toRemove = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                d.ServiceType == typeof(DbContextOptions) ||
                d.ServiceType == typeof(ApplicationDbContext))
                .ToList();
            foreach (var d in toRemove) services.Remove(d);

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase("acl-integration-tests"));
        });
    }

    /// <summary>Token carrying exactly the Permission claims the manifest grants <paramref name="roleAbbreviation"/>.</summary>
    public string MintRoleToken(string roleAbbreviation)
    {
        var claims = ManifestGrantsFor(roleAbbreviation)
            .Select(c => new ClaimDto(SystemClaims.PermissionClaimType, c))
            .ToList();
        return MintToken(roleAbbreviation, claims, roleAbbreviation);
    }

    /// <summary>
    /// Token carrying exactly the given Permission claims (and nothing else) — for testing
    /// fine-grained cells like the per-type Admin.Lookups.&lt;Type&gt;.Manage claims (D-10), which no
    /// seeded role holds, so they can't be exercised via <see cref="MintRoleToken"/>.
    /// </summary>
    public string MintWithPermissions(params string[] permissionClaims)
    {
        var claims = permissionClaims
            .Select(c => new ClaimDto(SystemClaims.PermissionClaimType, c))
            .ToList();
        return MintToken("CUSTOM", claims, "CUSTOM");
    }

    /// <summary>Token carrying the SYSADMIN god-mode wildcard ('System','FullAccess').</summary>
    public string MintWildcardToken()
    {
        var claims = new[] { new ClaimDto(SystemClaims.SystemClaimType, SystemClaims.FullAccessValue) };
        return MintToken("SYSADMIN", claims, "SYSADMIN");
    }

    private string MintToken(string roleAbbreviation, IReadOnlyCollection<ClaimDto> claims, string roleClaim)
    {
        using var scope = Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        return tokenService.CreateAccessToken(
            userId: 1,
            email: $"{roleAbbreviation.ToLowerInvariant()}@neurocorp.test",
            fullName: $"Test {roleAbbreviation}",
            claims: claims,
            roles: new[] { roleClaim },
            mustChangePassword: false).Token;
    }

    // ── manifest grants (mirrors the vendored access-control-matrix.json) ─────────────
    private static IReadOnlyList<string>? _claimsCache;
    private static readonly Dictionary<string, List<string>> GrantsByRole = new();

    private static IEnumerable<string> ManifestGrantsFor(string roleAbbreviation)
    {
        if (_claimsCache is null) LoadManifest();
        return GrantsByRole.TryGetValue(roleAbbreviation, out var grants) ? grants : Enumerable.Empty<string>();
    }

    private static void LoadManifest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "access-control-matrix.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var claims = new List<string>();
        foreach (var entry in doc.RootElement.GetProperty("claims").EnumerateArray())
        {
            var claim = entry.GetProperty("claim").GetString()!;
            claims.Add(claim);
            foreach (var grant in entry.GetProperty("grants").EnumerateArray())
            {
                var role = grant.GetString()!;
                if (!GrantsByRole.TryGetValue(role, out var list))
                    GrantsByRole[role] = list = new List<string>();
                list.Add(claim);
            }
        }
        _claimsCache = claims;
    }
}
