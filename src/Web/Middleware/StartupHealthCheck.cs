using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading;
using System.Threading.Tasks;

namespace Neurocorp.Api.Web.Middleware.HealthChecks;
public class StartupHealthCheck : IHealthCheck
{
    private volatile bool _isReady;
    private volatile bool _firstTimeInit;
    private DateTime? _initTimestamp;

    public bool StartupCompleted
    {
        get => _isReady;
        set => _isReady = value;
    }

    public DateTime? StartupTimestamp
    {
        get => _initTimestamp;
        private set => _initTimestamp = value;
    }


    public StartupHealthCheck()
    {
        StartupCompleted = false;
        _firstTimeInit = true;
    }

    public double MinutesSinceStart()
    {
        TimeSpan elapsed = DateTime.UtcNow - (StartupTimestamp ?? DateTime.UtcNow);
        return elapsed.TotalMinutes;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (StartupCompleted)
        {
            if (_firstTimeInit){
                StartupTimestamp = DateTime.UtcNow;
                _firstTimeInit = false;
            }
            return Task.FromResult(HealthCheckResult.Healthy(
                $"The startup task has completed on [{(StartupTimestamp ?? DateTime.UtcNow).ToShortDateString()}:{(StartupTimestamp ?? DateTime.UtcNow).ToShortTimeString()}]"));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy("The startup task is still running."));
    }
}
