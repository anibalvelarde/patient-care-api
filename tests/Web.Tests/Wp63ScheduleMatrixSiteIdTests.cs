using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Neurocorp.Api.Web.Tests.Authorization;
using Swashbuckle.AspNetCore.Swagger;

namespace Neurocorp.Api.Web.Tests;

/// <summary>
/// WP-63B (B4): schedule-matrix's siteId was a plain [FromQuery] int — Swagger advertised it as
/// optional, and a missing value bound to 0 and surfaced as a confusing 400 "Site 0 not found".
/// It is now [BindRequired]: required in the OpenAPI doc, and a missing value fails model
/// validation (a 400 ValidationProblemDetails naming siteId) before the service is reached.
/// </summary>
public class Wp63ScheduleMatrixSiteIdTests : IClassFixture<AccessControlTestFactory>
{
    private readonly AccessControlTestFactory _factory;

    public Wp63ScheduleMatrixSiteIdTests(AccessControlTestFactory factory) => _factory = factory;

    [Fact]
    public void Swagger_marks_siteId_as_required()
    {
        var doc = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var op = doc.Paths["/api/sessions/{dateString}/schedule-matrix"].Operations[OperationType.Get];
        op.Parameters.Should().Contain(p => p.Name == "siteId" && p.In == ParameterLocation.Query)
            .Which.Required.Should().BeTrue();
    }

    [Fact]
    public async Task Missing_siteId_is_a_model_validation_400_naming_the_parameter()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.MintRoleToken("MGR"));

        var response = await client.GetAsync("/api/sessions/2026-11-09/schedule-matrix");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue(body);
        errors.EnumerateObject().Select(e => e.Name)
            .Should().Contain(n => n.Equals("siteId", StringComparison.OrdinalIgnoreCase), body);
    }
}
