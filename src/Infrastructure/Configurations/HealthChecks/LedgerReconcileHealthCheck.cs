using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Configurations.HealthChecks;

/// <summary>
/// WP-58: proves the payment ledger and the sessions agree. Every session must satisfy
/// <c>AmountPaid = LegacyAmountPaid + SUM(SessionPayment.AmountAllocated)</c>
/// (V037 backfilled the legacy baseline so this holds on day one).
///
/// <para>A mismatch reports <b>Degraded</b>, not Unhealthy: it is a data problem to investigate,
/// not a reason to take the API out of service. The first offending session IDs are in the
/// result data so they can be looked up directly.</para>
/// </summary>
public class LedgerReconcileHealthCheck : IHealthCheck
{
    private const int SampleSize = 10;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<LedgerReconcileHealthCheck> _logger;

    public LedgerReconcileHealthCheck(ApplicationDbContext dbContext, ILogger<LedgerReconcileHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var mismatches = _dbContext.TherapySessions.AsNoTracking()
                .Where(s => s.AmountPaid != s.LegacyAmountPaid
                    + (_dbContext.SessionPayments
                        .Where(sp => sp.TherapySessionId == s.Id)
                        .Sum(sp => (decimal?)sp.AmountAllocated) ?? 0m));

            var count = await mismatches.CountAsync(cancellationToken);
            if (count == 0)
            {
                return HealthCheckResult.Healthy("Ledger reconciles: every session's AmountPaid matches its allocations.");
            }

            var sample = await mismatches.OrderBy(s => s.Id).Select(s => s.Id).Take(SampleSize).ToListAsync(cancellationToken);
            var data = new Dictionary<string, object>
            {
                ["mismatchCount"] = count,
                ["sampleSessionIds"] = string.Join(",", sample),
            };
            _logger.LogCritical(
                "Ledger reconcile: {Count} session(s) where AmountPaid != LegacyAmountPaid + allocations (first: {Sample}).",
                count, data["sampleSessionIds"]);
            return HealthCheckResult.Degraded(
                $"{count} session(s) where AmountPaid does not equal LegacyAmountPaid + allocated payments.", data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Ledger reconcile query failed.", ex);
        }
    }
}
