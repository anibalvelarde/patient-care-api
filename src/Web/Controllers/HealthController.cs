using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Reflection;
using Neurocorp.Api.Web.Middleware.HealthChecks;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly StartupHealthCheck _healthCheckStartup;
    private readonly HealthCheckService _healthCheckService;

    public HealthController(StartupHealthCheck startupHealthCheck, HealthCheckService healthCheckService)
    {
        _healthCheckStartup = startupHealthCheck;
        _healthCheckService = healthCheckService;
    }

    [HttpGet("live")]
    public async Task<IActionResult> GetLiveness()
    {
        var result = await _healthCheckService.CheckHealthAsync();
        if (result.Status.Equals(HealthStatus.Healthy))
        {
            return Ok("Alive");
        } else {
            return Ok("Not Alive");
        }
    }

    [HttpGet("startup")]
    public async Task<IActionResult> GetReadiness()
    {
        await Task.Yield();

        if (_healthCheckStartup.StartupCompleted)
        {
            if (_healthCheckStartup.StartupTimestamp.HasValue)
            {
                var ts = _healthCheckStartup.StartupTimestamp.Value;
                return Ok($"Started @ UTC: [{ts.ToShortDateString()} {ts.ToShortTimeString()}:{ts.Second}] |  Uptime (min): {_healthCheckStartup.MinutesSinceStart()}");
            } else {
                return Ok($"Started @ UTC: [N/A]");
            }
        }

        return StatusCode(503, "Not Started");
    }

    [HttpGet("ready")]
    public async Task<IActionResult> GetStartup()
    {
        var result = await _healthCheckService.CheckHealthAsync();
        if (result.Status.Equals(HealthStatus.Healthy))
        {
            return Ok("Ready");
        } else {
            return Ok("Not Ready");
        }
    }

    [HttpGet("checks")]
    public async Task<IActionResult> GetHealthChecks()
    {
        var healthReport = await _healthCheckService.CheckHealthAsync();
        var healthStatuses = healthReport.Entries.Select(entry => new
        {
            Name = entry.Key,
            Status = entry.Value.Status.ToString(),
            Description = entry.Value.Description,
            Data = entry.Value.Data
        }).ToList();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var result = new
        {
            Version = version,
            Statuses = healthStatuses
        };

        return Ok(result);
    }    
}