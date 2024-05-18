using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Neurocorp.Api.Infrastructure.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Neurocorp.Api.Infrastructure.Configurations.HealthChecks;
public class CustomDbHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _dbContext;

    public CustomDbHealthCheck(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Implement DB Health Check Logic...
            await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
            return HealthCheckResult.Healthy("Database is OK!");
        }
        catch (System.Exception)
        {
            return HealthCheckResult.Unhealthy("Database is in an unhealthy state.");
        }

    }
}
