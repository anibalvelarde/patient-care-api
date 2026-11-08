using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Neurocorp.Api.Web.Tests.Authorization;

namespace Neurocorp.Api.Web.Tests;

/// <summary>
/// WP-57 (prod bug, found 2026-11-07): WP-50C made caretaker email optional in the UI (blank →
/// field omitted), but CaretakerProfileRequest kept [Required] Email → every email-less caretaker
/// create was a 400 ("One or more validation errors occurred."). Runs through the real host
/// pipeline so [ApiController] model validation genuinely executes.
/// </summary>
public class CaretakerCreateValidationTests : IClassFixture<AccessControlTestFactory>
{
    private readonly AccessControlTestFactory _factory;

    public CaretakerCreateValidationTests(AccessControlTestFactory factory) => _factory = factory;

    private HttpClient CreateMgrClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.MintRoleToken("MGR"));
        return client;
    }

    [Theory]
    [InlineData("omit")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_WithoutEmail_Creates(string emailCase)
    {
        var client = CreateMgrClient();
        var lastName = $"NoEmail-{Guid.NewGuid().ToString("N")[..8]}";
        object body = emailCase switch
        {
            "omit" => new { firstName = "WP57", lastName, phoneNumber = "555-0100", isActive = true },
            "null" => new { firstName = "WP57", lastName, email = (string?)null, phoneNumber = "555-0100", isActive = true },
            _ => new { firstName = "WP57", lastName, email = emailCase, phoneNumber = "555-0100", isActive = true },
        };

        var response = await client.PostAsJsonAsync("/api/caretakers", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_WithEmail_StillCreates()
    {
        var client = CreateMgrClient();
        var tag = Guid.NewGuid().ToString("N")[..8];

        var response = await client.PostAsJsonAsync("/api/caretakers",
            new { firstName = "WP57", lastName = $"WithEmail-{tag}", email = $"wp57-{tag}@neurocorp.test", phoneNumber = "555-0100", isActive = true });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
