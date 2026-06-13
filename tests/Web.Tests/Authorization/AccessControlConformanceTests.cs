using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Neurocorp.Api.Web.Authorization;

namespace Neurocorp.Api.Web.Tests.Authorization;

/// <summary>
/// WP-17B-1: proves the API's authorization surface conforms to the access-control manifest
/// (vendored at docs/access-control-matrix.json, canonical source in patient-care-super).
///
/// Conformance checks:
///  1. The vendored manifest is internally consistent (recomputed semantic hash == recorded hash).
///  2. The code anchor (AccessControlManifest.SemanticHash) matches the vendored manifest.
///  3. Every [Authorize(Policy = "claim:...")] on a controller/action references a Permission
///     claim that exists in the manifest — no typos, no orphan policies.
///  4. [AllowAnonymous] appears only on the documented allowlist (login/refresh/health).
///  5. Every other action is inventoried: actions relying on the authenticated-only fallback
///     (no granular claim yet) must exactly match the checked-in baseline
///     (conformance-baseline.txt). Decorating an action ratchets the baseline down; a new
///     undecorated action fails the build until it is either decorated or deliberately baselined.
/// </summary>
public class AccessControlConformanceTests
{
    private const string PolicyPrefix = "claim:";
    private const string PermissionClaimType = "Permission";

    private static readonly string BaseDir = AppContext.BaseDirectory;
    private static readonly string ManifestPath = Path.Combine(BaseDir, "access-control-matrix.json");
    private static readonly string BaselinePath = Path.Combine(BaseDir, "conformance-baseline.txt");

    // ── 1 + 2: hash anchor integrity ─────────────────────────────────────────────

    [Fact]
    public void VendoredManifest_RecordedHash_MatchesRecomputedSemanticHash()
    {
        var manifest = LoadManifest();
        var recomputed = ComputeSemanticHash(manifest);

        recomputed.Should().Be(manifest.RecordedHash,
            "the vendored manifest's _meta.semanticHash must equal the hash recomputed from its " +
            "own grant tuples — if this fails the vendored file was hand-edited instead of " +
            "regenerated via patient-care-super/tools/generate-access-manifest.sh");
    }

    [Fact]
    public void CodeAnchor_Matches_VendoredManifestHash()
    {
        var manifest = LoadManifest();

        AccessControlManifest.SemanticHash.Should().Be(manifest.RecordedHash,
            "AccessControlManifest.SemanticHash must be bumped (deliberately, after re-review) " +
            "whenever the access-control matrix changes and the manifest is re-vendored");
    }

    [Fact]
    public void CanonicalManifestInSuperRepo_WhenPresent_MatchesVendoredCopy()
    {
        // Cross-repo drift check for dev machines where the sibling repo is checked out.
        // CI enforcement across repos is WP-17D; absence of the sibling is not a failure here.
        var canonicalPath = FindSiblingManifest();
        if (canonicalPath is null) return;

        var canonical = JsonDocument.Parse(File.ReadAllText(canonicalPath))
            .RootElement.GetProperty("_meta").GetProperty("semanticHash").GetString();

        canonical.Should().Be(AccessControlManifest.SemanticHash,
            $"the canonical manifest at {canonicalPath} has moved on — re-vendor " +
            "docs/access-control-matrix.json and bump AccessControlManifest.SemanticHash");
    }

    // ── 2b: generated Permissions constants mirror the manifest (codegen no-drift) ──

    [Fact]
    public void PermissionsConstants_ExactlyMatch_ManifestClaims()
    {
        var manifest = LoadManifest();
        var constants = typeof(Permissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();

        constants.Should().BeEquivalentTo(manifest.Claims,
            "the generated Permissions constants must equal the manifest's claim set exactly — " +
            "regenerate via tools/generate-permissions.sh after any matrix change; a missing or " +
            "extra constant means Permissions.g.cs drifted from the manifest");
    }

    // ── 3: every claim policy resolves to a manifest claim ──────────────────────

    [Fact]
    public void EveryClaimPolicy_References_AManifestClaim()
    {
        var manifest = LoadManifest();
        var violations = new List<string>();

        foreach (var action in EnumerateActions())
        {
            foreach (var policy in action.ClaimPolicies)
            {
                var spec = policy.Substring(PolicyPrefix.Length);
                var separator = spec.IndexOf(':');
                var claimType = separator < 0 ? PermissionClaimType : spec[..separator];
                var claimValue = separator < 0 ? spec : spec[(separator + 1)..];

                if (claimType != PermissionClaimType)
                    violations.Add($"{action.Id}: policy '{policy}' uses claim type '{claimType}' — " +
                                   "only Permission claims are governed by the matrix");
                else if (!manifest.Claims.Contains(claimValue))
                    violations.Add($"{action.Id}: policy '{policy}' references claim '{claimValue}' " +
                                   "which does not exist in the access-control manifest");
            }
        }

        violations.Should().BeEmpty(
            "every [Authorize(Policy = \"claim:...\")] must reference a claim defined in the " +
            "access-control matrix; add the capability to the matrix (and regenerate the manifest) " +
            "or fix the typo");
    }

    // ── 4: anonymous endpoints are a closed set ─────────────────────────────────

    private static readonly HashSet<string> AnonymousAllowlist = new()
    {
        "HealthController",            // class-level: k8s probes
        "AuthController.Login",
        "AuthController.Refresh",
    };

    [Fact]
    public void AllowAnonymous_AppearsOnlyOnTheAllowlist()
    {
        var offenders = EnumerateActions()
            .Where(a => a.IsAnonymous)
            .Where(a => !AnonymousAllowlist.Contains(a.Id) &&
                        !AnonymousAllowlist.Contains(a.Controller))
            .Select(a => a.Id)
            .ToList();

        offenders.Should().BeEmpty(
            "[AllowAnonymous] disables the secure-by-default fallback policy; new anonymous " +
            "endpoints must be deliberately added to the allowlist in this test");
    }

    // ── 5: undecorated-action baseline (ratchet) ─────────────────────────────────

    [Fact]
    public void ActionsWithoutClaimPolicies_ExactlyMatchTheBaseline()
    {
        var undecorated = EnumerateActions()
            .Where(a => !a.IsAnonymous && a.ClaimPolicies.Count == 0)
            .Select(a => a.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        // Keep an up-to-date copy next to the baseline so updating it is a file copy, not typing.
        File.WriteAllLines(Path.Combine(BaseDir, "conformance-baseline.actual.txt"), undecorated);

        File.Exists(BaselinePath).Should().BeTrue(
            $"conformance-baseline.txt is missing; seed it from conformance-baseline.actual.txt in {BaseDir}");

        var baseline = File.ReadAllLines(BaselinePath)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var newlyUndecorated = undecorated.Except(baseline).ToList();
        var decoratedButBaselined = baseline.Except(undecorated).ToList();

        newlyUndecorated.Should().BeEmpty(
            "these actions rely on the authenticated-only fallback but are not in the accepted " +
            "baseline — give them a claim policy from the matrix, or (deliberately) add them to " +
            "conformance-baseline.txt");
        decoratedButBaselined.Should().BeEmpty(
            "these baseline entries no longer exist undecorated (renamed, removed, or now " +
            "decorated — good!) — remove them from conformance-baseline.txt so the ratchet " +
            "only moves down");
    }

    // ── plumbing ─────────────────────────────────────────────────────────────────

    private sealed record ManifestData(string RecordedHash, HashSet<string> Claims,
        List<(string Claim, List<string> Grants)> Tuples);

    private static ManifestData LoadManifest()
    {
        File.Exists(ManifestPath).Should().BeTrue(
            $"vendored manifest not found at {ManifestPath} — check Web.Tests.csproj content items");

        using var doc = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        var root = doc.RootElement;
        var hash = root.GetProperty("_meta").GetProperty("semanticHash").GetString()!;

        var tuples = new List<(string, List<string>)>();
        foreach (var entry in root.GetProperty("claims").EnumerateArray())
        {
            var claim = entry.GetProperty("claim").GetString()!;
            var grants = entry.GetProperty("grants").EnumerateArray()
                .Select(g => g.GetString()!).ToList();
            tuples.Add((claim, grants));
        }

        return new ManifestData(hash, tuples.Select(t => t.Item1).ToHashSet(), tuples);
    }

    /// <summary>
    /// Mirrors tools/generate-access-manifest.sh: sha256 (first 12 hex) over the compact JSON
    /// {"claims":[{"claim":...,"grants":[...]},...]} with claims and grants sorted ordinally.
    /// </summary>
    private static string ComputeSemanticHash(ManifestData manifest)
    {
        var sb = new StringBuilder("{\"claims\":[");
        var first = true;
        foreach (var (claim, grants) in manifest.Tuples.OrderBy(t => t.Claim, StringComparer.Ordinal))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"claim\":\"").Append(claim).Append("\",\"grants\":[");
            sb.Append(string.Join(',', grants.OrderBy(g => g, StringComparer.Ordinal)
                .Select(g => $"\"{g}\"")));
            sb.Append("]}");
        }
        sb.Append("]}");

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(digest)[..12].ToLowerInvariant();
    }

    private sealed record ActionInfo(string Controller, string Id, bool IsAnonymous,
        List<string> ClaimPolicies);

    private static IEnumerable<ActionInfo> EnumerateActions()
    {
        var webAssembly = typeof(AccessControlManifest).Assembly;
        var controllers = webAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && t.IsPublic);

        foreach (var controller in controllers)
        {
            var classAnonymous = controller.GetCustomAttributes<AllowAnonymousAttribute>(true).Any();
            var classPolicies = ClaimPoliciesOf(controller.GetCustomAttributes<AuthorizeAttribute>(true));

            // Attribute routing (MapControllers): only methods carrying an [Http*] verb are endpoints.
            var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.GetCustomAttributes<HttpMethodAttribute>(true).Any());

            foreach (var action in actions)
            {
                var policies = classPolicies
                    .Concat(ClaimPoliciesOf(action.GetCustomAttributes<AuthorizeAttribute>(true)))
                    .Distinct()
                    .ToList();
                var anonymous = classAnonymous ||
                    action.GetCustomAttributes<AllowAnonymousAttribute>(true).Any();

                yield return new ActionInfo(
                    controller.Name,
                    $"{controller.Name}.{action.Name}",
                    anonymous,
                    policies);
            }
        }
    }

    private static List<string> ClaimPoliciesOf(IEnumerable<AuthorizeAttribute> attributes) =>
        attributes.Select(a => a.Policy)
            .Where(p => p is not null && p.StartsWith(PolicyPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(p => p!)
            .ToList();

    private static string? FindSiblingManifest()
    {
        // Walk up from the test output dir looking for the sibling super repo checkout.
        var dir = new DirectoryInfo(BaseDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "..", "patient-care-super",
                "docs", "access-control-matrix.json");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            dir = dir.Parent;
        }
        return null;
    }
}
