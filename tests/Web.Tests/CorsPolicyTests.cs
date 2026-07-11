using System.Net;
using FluentAssertions;
using Neurocorp.Api.Web.Tests.Authorization;

namespace Neurocorp.Api.Web.Tests;

/// <summary>
/// CORS origin-allowlist regression tests. *.cloudfront.net must NOT be wildcard-allowed:
/// any AWS account can mint a distribution under that suffix, and the policy also sends
/// AllowCredentials. Only the clinic's own pinned distribution host may pass.
/// Exercises the real pipeline via an anonymous endpoint (health) so only CORS is in play.
/// </summary>
public class CorsPolicyTests : IClassFixture<AccessControlTestFactory>
{
    private readonly AccessControlTestFactory _factory;

    public CorsPolicyTests(AccessControlTestFactory factory) => _factory = factory;

    private async Task<HttpResponseMessage> GetWithOrigin(string origin)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health/checks");
        request.Headers.Add("Origin", origin);
        return await client.SendAsync(request);
    }

    [Theory]
    [InlineData("https://d26wxxuffdufr.cloudfront.net")]  // the clinic's pinned distribution
    [InlineData("http://localhost:8080")]                  // local dev UI
    public async Task AllowedOrigin_GetsCorsHeader(string origin)
    {
        var response = await GetWithOrigin(origin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("Access-Control-Allow-Origin")
            .WhoseValue.Should().ContainSingle().Which.Should().Be(origin);
    }

    [Theory]
    [InlineData("https://d1111attacker.cloudfront.net")]  // arbitrary third-party distribution
    [InlineData("https://evil.example.com")]
    public async Task ForeignOrigin_GetsNoCorsHeader(string origin)
    {
        var response = await GetWithOrigin(origin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}
