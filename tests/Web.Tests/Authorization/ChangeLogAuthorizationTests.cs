using System.Net;
using FluentAssertions;

namespace Neurocorp.Api.Web.Tests.Authorization;

/// <summary>
/// WP-54B: end-to-end claim gating for /api/admin/change-log (real pipeline via
/// AccessControlTestFactory). Both claims are SYSADMIN-only (empty granular grants):
///   Admin.ChangeLog.View  → the three GETs
///   Admin.ChangeLog.Purge → the DELETE
/// The two are SEPARATE policies, so a View grant can never purge (and vice-versa) — proven with
/// MintWithPermissions tokens that carry exactly one of the two.
/// </summary>
public class ChangeLogAuthorizationTests : IClassFixture<AccessControlTestFactory>
{
    private const string Summary = "/api/admin/change-log/summary";
    private const string List = "/api/admin/change-log";
    private const string Types = "/api/admin/change-log/entity-types";
    private const string Purge = "/api/admin/change-log"; // DELETE

    private readonly AccessControlTestFactory _factory;

    public ChangeLogAuthorizationTests(AccessControlTestFactory factory) => _factory = factory;

    [Theory]
    [InlineData("GET", Summary, "MGR")]
    [InlineData("GET", Summary, "AM")]
    [InlineData("GET", Summary, "FD")]
    [InlineData("GET", Summary, "OWN")]
    [InlineData("GET", Summary, "ACCT")]
    [InlineData("GET", List, "MGR")]
    [InlineData("GET", List, "OWN")]
    [InlineData("GET", Types, "MGR")]
    [InlineData("GET", Types, "ACCT")]
    [InlineData("DELETE", Purge, "MGR")]
    [InlineData("DELETE", Purge, "OWN")]
    [InlineData("DELETE", Purge, "ACCT")]
    [InlineData("GET", Summary, "CARETAKER")]
    public async Task GranularRole_Is403(string method, string route, string role)
    {
        var response = await SendAsync(method, route, _factory.MintRoleToken(role));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"{role} holds neither Admin.ChangeLog.View nor .Purge ({method} {route})");
    }

    [Theory]
    [InlineData("GET", Summary)]
    [InlineData("GET", List)]
    [InlineData("GET", Types)]
    [InlineData("DELETE", Purge)]
    public async Task Sysadmin_IsNotBlocked(string method, string route)
    {
        var response = await SendAsync(method, route, _factory.MintWildcardToken());
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            $"the SYSADMIN wildcard satisfies {method} {route}");
    }

    [Theory]
    [InlineData("GET", Summary)]
    [InlineData("GET", List)]
    [InlineData("GET", Types)]
    [InlineData("DELETE", Purge)]
    public async Task Unauthenticated_Is401(string method, string route)
    {
        var response = await SendAsync(method, route, token: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ViewClaim_CanRead_ButCannotPurge()
    {
        var token = _factory.MintWithPermissions("Admin.ChangeLog.View");

        (await SendAsync("GET", Summary, token)).StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        (await SendAsync("GET", List, token)).StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        (await SendAsync("DELETE", Purge, token)).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "Admin.ChangeLog.View must NOT satisfy the purge policy");
    }

    [Fact]
    public async Task PurgeClaim_CanPurge_ButCannotRead()
    {
        var token = _factory.MintWithPermissions("Admin.ChangeLog.Purge");

        (await SendAsync("DELETE", Purge, token)).StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        (await SendAsync("GET", Summary, token)).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "Admin.ChangeLog.Purge must NOT satisfy the read policy");
    }

    private async Task<HttpResponseMessage> SendAsync(string method, string route, string? token)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        return await client.SendAsync(request);
    }
}
