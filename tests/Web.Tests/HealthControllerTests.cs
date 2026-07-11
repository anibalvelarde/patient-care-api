using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Neurocorp.Api.Web;
using Neurocorp.Api.Web.Controllers;
using Neurocorp.Api.Web.Middleware.HealthChecks;
using Neurocorp.Api.Web.Tests.Authorization;

namespace Web.Tests;

public class HealthControllerTests
{
    [Fact]
    public void BuildInfo_TimestampIsStampedAndParseableIso8601Utc()
    {
        // H2: the assembly must carry a compile-time build timestamp
        // (AssemblyMetadata "BuildTimestampUtc" in Web.csproj).
        Assert.NotEqual("unknown", BuildInfo.BuildTimestampUtc);

        var parsed = DateTimeOffset.TryParse(
            BuildInfo.BuildTimestampUtc,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var timestamp);

        Assert.True(parsed, $"BuildTimestampUtc '{BuildInfo.BuildTimestampUtc}' is not a parseable ISO-8601 timestamp");
        Assert.Equal(TimeSpan.Zero, timestamp.Offset);
    }

    [Fact]
    public async Task GetHealthChecks_IncludesVersionAndBuildTimeUtc()
    {
        var healthCheckService = new Mock<HealthCheckService>();
        healthCheckService
            .Setup(s => s.CheckHealthAsync(
                It.IsAny<Func<HealthCheckRegistration, bool>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthReport(
                new Dictionary<string, HealthReportEntry>(),
                TimeSpan.Zero));

        var controller = new HealthController(new StartupHealthCheck(), healthCheckService.Object);

        var response = await controller.GetHealthChecks();

        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.NotNull(ok.Value);
        var payloadType = ok.Value!.GetType();

        var version = payloadType.GetProperty("Version")?.GetValue(ok.Value) as string;
        Assert.False(string.IsNullOrEmpty(version));

        var buildTime = payloadType.GetProperty("BuildTimeUtc")?.GetValue(ok.Value) as string;
        Assert.Equal(BuildInfo.BuildTimestampUtc, buildTime);
    }
}

/// <summary>
/// Wire-level check through the real host: the UI reads camelCase `buildTimeUtc` off
/// GET /api/health/checks (contract health-api.md), so assert the serialized JSON, not the
/// controller's anonymous object.
/// </summary>
public class HealthChecksEndpointTests : IClassFixture<AccessControlTestFactory>
{
    private readonly AccessControlTestFactory _factory;

    public HealthChecksEndpointTests(AccessControlTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetHealthChecks_SerializesBuildTimeUtcInCamelCase()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health/checks");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.True(json.RootElement.TryGetProperty("version", out _));
        Assert.True(json.RootElement.TryGetProperty("buildTimeUtc", out var buildTime));
        Assert.Equal(BuildInfo.BuildTimestampUtc, buildTime.GetString());
    }
}
