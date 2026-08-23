using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.Authorization;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Configurations.HealthChecks;

/// <summary>
/// WP-55 B-3 (G2): verifies the centralized lookup constants still match the live seed rows —
/// every <see cref="SessionStatus"/> id↔name pairing against <c>AppointmentStatus</c>, and the
/// three <see cref="RoleTaxonomy"/> identity role ids against <c>RoleType</c>. This is the runtime
/// counterpart to the build-time B-4 guards: it catches a seed edit (a status renamed, an id
/// reassigned — the B-2d "7 = Owner vs FrontDesk" class of drift) that ships fine but silently
/// disagrees with the code.
///
/// Mirrors the WP-54 <c>changelog</c> health check: it probes on demand and reports Unhealthy
/// rather than killing the process, because the LAN DB can lag at boot (EnableRetryOnFailure) — a
/// transient unreachable DB should not crash-loop the pod. A genuine mismatch is logged at
/// <b>Critical</b> so it is loud, and surfaces on <c>/api/health/checks</c> as <c>lookupSeed</c>.
/// </summary>
public class LookupSeedHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<LookupSeedHealthCheck> _logger;

    public LookupSeedHealthCheck(ApplicationDbContext dbContext, ILogger<LookupSeedHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        Dictionary<int, string> statusRows;
        Dictionary<int, string> roleRows;
        try
        {
            statusRows = await _dbContext.Set<AppointmentStatus>().AsNoTracking()
                .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);
            roleRows = await _dbContext.Set<RoleType>().AsNoTracking()
                .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);
        }
        catch (Exception ex)
        {
            // Unreachable / lagging DB at boot — not a seed mismatch. Non-fatal.
            return HealthCheckResult.Unhealthy("Lookup tables not reachable.", ex);
        }

        var mismatches = new List<string>();

        foreach (var (id, name) in SessionStatus.IdToName)
        {
            if (!statusRows.TryGetValue(id, out var actual))
                mismatches.Add($"AppointmentStatus id {id} ({name}) missing from the seed");
            else if (!string.Equals(actual, name, StringComparison.Ordinal))
                mismatches.Add($"AppointmentStatus id {id}: code='{name}' but seed='{actual}'");
        }

        var expectedRoles = new Dictionary<int, string>
        {
            [RoleTaxonomy.TherapistRoleId] = "Therapist",
            [RoleTaxonomy.PatientRoleId] = "Patient",
            [RoleTaxonomy.CaretakerRoleId] = "Caretaker",
        };
        foreach (var (id, name) in expectedRoles)
        {
            if (!roleRows.TryGetValue(id, out var actual))
                mismatches.Add($"RoleType id {id} ({name}) missing from the seed");
            else if (!string.Equals(actual, name, StringComparison.Ordinal))
                mismatches.Add($"RoleType id {id}: code='{name}' but seed='{actual}'");
        }

        if (mismatches.Count == 0)
            return HealthCheckResult.Healthy("Lookup constants match the seed (AppointmentStatus, RoleType).");

        var detail = string.Join("; ", mismatches);
        _logger.LogCritical(
            "Lookup seed mismatch — the DB lookup rows disagree with the code constants: {Mismatches}. " +
            "Fix the seed or the constants (SessionStatus / RoleTaxonomy) before trusting id-based logic.",
            detail);

        var data = mismatches
            .Select((m, i) => (Key: $"mismatch_{i}", Value: (object)m))
            .ToDictionary(x => x.Key, x => x.Value);
        return HealthCheckResult.Unhealthy($"Lookup seed disagrees with code constants: {detail}", data: data);
    }
}
