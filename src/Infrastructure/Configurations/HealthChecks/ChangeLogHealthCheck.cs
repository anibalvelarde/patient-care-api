using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Neurocorp.Api.Core.Configurations;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Configurations.HealthChecks;

/// <summary>
/// WP-54 D9: reports whether the EntityChangeLog table is reachable and whether capture is enabled.
/// Makes a skipped deploy step (table missing) or a flipped kill-switch visible on /api/health/checks.
/// </summary>
public class ChangeLogHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IOptions<ChangeLogOptions> _options;

    public ChangeLogHealthCheck(ApplicationDbContext dbContext, IOptions<ChangeLogOptions> options)
    {
        _dbContext = dbContext;
        _options = options;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var enabled = _options.Value.Enabled;
        var data = new Dictionary<string, object> { ["enabled"] = enabled };
        try
        {
            // Cheap reachability probe against the table (SELECT EXISTS).
            await _dbContext.Set<EntityChangeLog>().AsNoTracking().AnyAsync(cancellationToken);
            return HealthCheckResult.Healthy(
                enabled ? "Change log reachable; capture enabled." : "Change log reachable; capture DISABLED (kill-switch).",
                data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Change log table not reachable.", ex, data);
        }
    }
}
