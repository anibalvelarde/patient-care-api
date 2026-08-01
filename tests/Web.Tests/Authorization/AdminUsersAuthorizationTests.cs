using System.Net;
using System.Text;
using FluentAssertions;

namespace Neurocorp.Api.Web.Tests.Authorization;

/// <summary>
/// WP-41B: end-to-end claim gating for /api/admin/users (real pipeline via
/// AccessControlTestFactory — JWT validation + PermissionPolicyProvider + handler).
///
/// Matrix (hash f82cab8c9efd — seeded since WP-17, NO change in this WP):
///   Admin.Users.View   [MGR, OWN]  → GET list, GET by id
///   Admin.Users.Manage []          → POST, PUT, POST reset-password — EMPTY grants row:
///                                    only the SYSADMIN ('System','FullAccess') wildcard
///                                    satisfies it; every granular role is denied.
/// </summary>
public class AdminUsersAuthorizationTests : IClassFixture<AccessControlTestFactory>
{
    private readonly AccessControlTestFactory _factory;

    public AdminUsersAuthorizationTests(AccessControlTestFactory factory) => _factory = factory;

    [Theory]
    // Admin.Users.View [MGR, OWN] — AM/FD/ACCT and restricted roles denied.
    [InlineData("GET", "/api/admin/users", "AM")]
    [InlineData("GET", "/api/admin/users", "FD")]
    [InlineData("GET", "/api/admin/users", "ACCT")]
    [InlineData("GET", "/api/admin/users", "CARETAKER")]
    [InlineData("GET", "/api/admin/users/1", "AM")]
    [InlineData("GET", "/api/admin/users/1", "FD")]
    // Admin.Users.Manage [] — SYSADMIN-only: even MGR/OWN (who can View) are denied writes.
    [InlineData("POST", "/api/admin/users", "MGR")]
    [InlineData("POST", "/api/admin/users", "OWN")]
    [InlineData("POST", "/api/admin/users", "AM")]
    [InlineData("POST", "/api/admin/users", "FD")]
    [InlineData("POST", "/api/admin/users", "ACCT")]
    [InlineData("PUT", "/api/admin/users/1", "MGR")]
    [InlineData("PUT", "/api/admin/users/1", "OWN")]
    [InlineData("POST", "/api/admin/users/1/reset-password", "MGR")]
    [InlineData("POST", "/api/admin/users/1/reset-password", "OWN")]
    public async Task RoleWithoutClaim_Is403(string method, string route, string role)
    {
        var response = await SendAsync(method, route, _factory.MintRoleToken(role));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"{role} lacks the matrix claim guarding {method} {route}");
    }

    [Theory]
    // Admin.Users.View — MGR and OWN granted; SYSADMIN via wildcard.
    [InlineData("GET", "/api/admin/users", "MGR")]
    [InlineData("GET", "/api/admin/users", "OWN")]
    [InlineData("GET", "/api/admin/users", "SYSADMIN")]
    [InlineData("GET", "/api/admin/users/1", "MGR")]
    [InlineData("GET", "/api/admin/users/1", "OWN")]
    [InlineData("GET", "/api/admin/users/1", "SYSADMIN")]
    // Admin.Users.Manage — the wildcard passes the gate (downstream 400/404 is fine:
    // authorization ran first and let it through).
    [InlineData("POST", "/api/admin/users", "SYSADMIN")]
    [InlineData("PUT", "/api/admin/users/1", "SYSADMIN")]
    [InlineData("POST", "/api/admin/users/1/reset-password", "SYSADMIN")]
    public async Task RoleWithClaim_IsNotBlocked(string method, string route, string role)
    {
        var token = role == "SYSADMIN" ? _factory.MintWildcardToken() : _factory.MintRoleToken(role);

        var response = await SendAsync(method, route, token);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            $"{role} presented a valid token for {method} {route}");
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            $"{role} holds the matrix claim guarding {method} {route}");
    }

    [Theory]
    [InlineData("GET", "/api/admin/users")]
    [InlineData("GET", "/api/admin/users/1")]
    [InlineData("POST", "/api/admin/users")]
    [InlineData("PUT", "/api/admin/users/1")]
    [InlineData("POST", "/api/admin/users/1/reset-password")]
    public async Task Unauthenticated_Is401(string method, string route)
    {
        var response = await SendAsync(method, route, token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            $"{method} {route} must reject anonymous callers");
    }

    private async Task<HttpResponseMessage> SendAsync(string method, string route, string? token)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        if (method is "POST" or "PUT")
        {
            // Empty JSON body: enough to clear model binding; semantics 400/404 downstream are
            // acceptable outcomes for the "not blocked" assertions.
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }
        return await client.SendAsync(request);
    }
}
