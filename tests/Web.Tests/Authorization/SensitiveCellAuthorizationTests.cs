using System.Net;
using System.Text;
using FluentAssertions;

namespace Neurocorp.Api.Web.Tests.Authorization;

/// <summary>
/// WP-17B-2: end-to-end authorization checks for the decorated sensitive-cell endpoints.
/// Each test mints a per-role token (carrying exactly that role's manifest claims) and asserts
/// the HTTP outcome matches the access-control matrix:
///   * a role WITHOUT the required claim is rejected with 403 Forbidden (authorization runs
///     before the action, so no database/controller work happens);
///   * a role WITH the claim is NOT blocked (200/400/404/500 are all acceptable — we only assert
///     authorization let it through, i.e. neither 401 nor 403);
///   * an unauthenticated request is 401.
///
/// SYSADMIN passes everything via the ('System','FullAccess') wildcard.
///
/// Covered cells (claim → granted roles):
///   Admin.Sites.View      [MGR]        GET  /api/sites, /api/sites/{id}
///   Admin.Sites.Manage    [SYSADMIN]   POST /api/sites, PUT /api/sites/{id}
///   Statements.Caretaker.View [AM,MGR] GET  /api/caretakers/{id}/statement
///   Statements.Therapist.View [AM,MGR] GET  /api/therapists/{id}/statement
///   Patients.Delinquent.View  [AM,MGR] GET  /api/patients/pastdue, /api/patients/{id}/pastdue
///   Therapists.Delinquent.View [AM,MGR] GET /api/therapists/{id}/pastdue
/// </summary>
public class SensitiveCellAuthorizationTests : IClassFixture<AccessControlTestFactory>
{
    private readonly AccessControlTestFactory _factory;

    public SensitiveCellAuthorizationTests(AccessControlTestFactory factory) => _factory = factory;

    [Theory]
    // Admin.Sites.View — granted to MGR only ⇒ AM and FD denied.
    [InlineData("GET", "/api/sites", "AM")]
    [InlineData("GET", "/api/sites", "FD")]
    [InlineData("GET", "/api/sites/1", "AM")]
    [InlineData("GET", "/api/sites/1", "FD")]
    // Admin.Sites.Manage — SYSADMIN only ⇒ MGR, AM, FD all denied.
    [InlineData("POST", "/api/sites", "MGR")]
    [InlineData("POST", "/api/sites", "AM")]
    [InlineData("POST", "/api/sites", "FD")]
    [InlineData("PUT", "/api/sites/1", "MGR")]
    [InlineData("PUT", "/api/sites/1", "AM")]
    [InlineData("PUT", "/api/sites/1", "FD")]
    // Statements / Delinquent — granted to AM,MGR ⇒ FD denied.
    [InlineData("GET", "/api/caretakers/1/statement", "FD")]
    [InlineData("GET", "/api/therapists/1/statement", "FD")]
    [InlineData("GET", "/api/patients/pastdue", "FD")]
    [InlineData("GET", "/api/patients/1/pastdue", "FD")]
    [InlineData("GET", "/api/therapists/1/pastdue", "FD")]
    public async Task RoleWithoutClaim_Is403(string method, string route, string role)
    {
        var response = await SendAsync(method, route, TokenFor(role));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"{role} lacks the matrix claim guarding {method} {route}");
    }

    [Theory]
    // Admin.Sites.View — MGR granted; SYSADMIN via wildcard.
    [InlineData("GET", "/api/sites", "MGR")]
    [InlineData("GET", "/api/sites", "SYSADMIN")]
    [InlineData("GET", "/api/sites/1", "SYSADMIN")]
    // Admin.Sites.Manage — only SYSADMIN.
    [InlineData("POST", "/api/sites", "SYSADMIN")]
    [InlineData("PUT", "/api/sites/1", "SYSADMIN")]
    // Statements / Delinquent — AM and MGR granted.
    [InlineData("GET", "/api/caretakers/1/statement", "AM")]
    [InlineData("GET", "/api/caretakers/1/statement", "MGR")]
    [InlineData("GET", "/api/therapists/1/statement", "AM")]
    [InlineData("GET", "/api/therapists/1/statement", "MGR")]
    [InlineData("GET", "/api/patients/pastdue", "AM")]
    [InlineData("GET", "/api/patients/pastdue", "MGR")]
    [InlineData("GET", "/api/patients/1/pastdue", "AM")]
    [InlineData("GET", "/api/therapists/1/pastdue", "MGR")]
    public async Task RoleWithClaim_IsNotBlocked(string method, string route, string role)
    {
        var response = await SendAsync(method, route, TokenFor(role));

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            $"{role} holds the matrix claim guarding {method} {route}");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "a valid token must authenticate");
    }

    [Theory]
    [InlineData("GET", "/api/sites")]
    [InlineData("POST", "/api/sites")]
    [InlineData("GET", "/api/caretakers/1/statement")]
    [InlineData("GET", "/api/patients/pastdue")]
    public async Task NoToken_Is401(string method, string route)
    {
        var response = await SendAsync(method, route, token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the secure-by-default fallback policy requires an authenticated user");
    }

    private string TokenFor(string role) =>
        role == "SYSADMIN" ? _factory.MintWildcardToken() : _factory.MintRoleToken(role);

    private async Task<HttpResponseMessage> SendAsync(string method, string route, string? token)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (token is not null)
            request.Headers.Authorization = new("Bearer", token);
        if (method is "POST" or "PUT")
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }
}
