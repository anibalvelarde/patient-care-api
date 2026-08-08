using System.Net;
using System.Text;
using FluentAssertions;

namespace Neurocorp.Api.Web.Tests.Authorization;

/// <summary>
/// WP-49 (BR3/BR4): end-to-end claim gating AND request binding for the fee endpoints, through
/// the real pipeline (JWT validation → PermissionPolicyProvider → model binding → controller).
///
/// <para><b>Why the binding half exists.</b> WP-49 shipped <c>WaiveFeeRequest.FeeKind</c> typed as
/// the <c>SessionFeeKind</c> enum. The API registers no <c>JsonStringEnumConverter</c>, so
/// System.Text.Json binds enums from INTEGERS only, and every waive call from the UI — which
/// sends <c>"feeKind":"Late"</c> — failed with a model-validation 400 naming a JSON path and a
/// byte offset. Nothing caught it: the service unit tests construct the DTO in C#, and the UI
/// specs mock the HTTP client, so the JSON boundary the bug lived on was never crossed by a
/// test. These cases cross it.</para>
///
/// Matrix (hash 6314adb59131):
///   Patients.Delinquent.View  [MGR, AM, OWN]  → GET late-fees/preview
///   Sessions.Fee.Manage       [MGR]           → POST late-fees/batch, POST {id}/waive-fee
/// </summary>
public class SessionFeesAuthorizationTests : IClassFixture<AccessControlTestFactory>
{
    private readonly AccessControlTestFactory _factory;

    public SessionFeesAuthorizationTests(AccessControlTestFactory factory) => _factory = factory;

    private const string WaiveBody = """{"feeKind":"Late","reason":"caretaker hospitalized"}""";
    private const string BatchBody = """{"sessionIds":[1]}""";

    private async Task<HttpResponseMessage> SendAsync(string method, string route, string token, string? body = null)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(method), route);
        request.Headers.Add("Authorization", $"Bearer {token}");
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        return await client.SendAsync(request);
    }

    // ── the JSON contract ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Late")]
    [InlineData("NoShow")]
    [InlineData("Both")]
    [InlineData("late")]     // case-insensitive
    public async Task WaiveFee_AcceptsTheFeeKindSTRING_OverTheWire(string feeKind)
    {
        // The regression guard. A 400 here means the request no longer BINDS — which is how
        // WP-49 originally shipped. Any other status means binding succeeded and the request
        // reached the service, which is all this test claims.
        var body = $$"""{"feeKind":"{{feeKind}}","reason":"caretaker hospitalized"}""";

        var response = await SendAsync("POST", "/api/sessions/1/waive-fee", _factory.MintRoleToken("MGR"), body);

        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest,
            $"feeKind \"{feeKind}\" must bind as a string — the UI sends exactly this shape");
    }

    [Fact]
    public async Task WaiveFee_IntegerFeeKind_IsNotTheContract()
    {
        // 1 was what the enum binder demanded. It is not a documented wire value, and the UI
        // never sends it — pinned so nobody "restores" integer binding thinking it is used.
        var response = await SendAsync("POST", "/api/sessions/1/waive-fee",
            _factory.MintRoleToken("MGR"), """{"feeKind":1,"reason":"x"}""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WaiveFee_MissingReason_Is400()
    {
        var response = await SendAsync("POST", "/api/sessions/1/waive-fee",
            _factory.MintRoleToken("MGR"), """{"feeKind":"Late"}""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LateFeeBatch_BindsItsRequestBody()
    {
        var response = await SendAsync("POST", "/api/sessions/late-fees/batch",
            _factory.MintRoleToken("MGR"), BatchBody);

        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
    }

    // ── claim gating ─────────────────────────────────────────────────────────────────────

    [Theory]
    // Sessions.Fee.Manage [MGR] — AM is the case BR4 is about: it lost waiver authority.
    [InlineData("POST", "/api/sessions/late-fees/batch", "AM", BatchBody)]
    [InlineData("POST", "/api/sessions/late-fees/batch", "FD", BatchBody)]
    [InlineData("POST", "/api/sessions/late-fees/batch", "OWN", BatchBody)]
    [InlineData("POST", "/api/sessions/late-fees/batch", "ACCT", BatchBody)]
    [InlineData("POST", "/api/sessions/1/waive-fee", "AM", WaiveBody)]
    [InlineData("POST", "/api/sessions/1/waive-fee", "FD", WaiveBody)]
    [InlineData("POST", "/api/sessions/1/waive-fee", "OWN", WaiveBody)]
    [InlineData("POST", "/api/sessions/1/waive-fee", "ACCT", WaiveBody)]
    // Patients.Delinquent.View — FD and ACCT cannot even preview.
    [InlineData("GET", "/api/sessions/late-fees/preview", "FD", null)]
    [InlineData("GET", "/api/sessions/late-fees/preview", "ACCT", null)]
    public async Task RoleWithoutClaim_Is403(string method, string route, string role, string? body)
    {
        var response = await SendAsync(method, route, _factory.MintRoleToken(role), body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"{role} lacks the matrix claim guarding {method} {route}");
    }

    [Theory]
    // Preview is a receivables read — AM and OWN can look at what is owed…
    [InlineData("GET", "/api/sessions/late-fees/preview", "MGR", null)]
    [InlineData("GET", "/api/sessions/late-fees/preview", "AM", null)]
    [InlineData("GET", "/api/sessions/late-fees/preview", "OWN", null)]
    // …but only MGR can fire the charge or forgive one.
    [InlineData("POST", "/api/sessions/late-fees/batch", "MGR", BatchBody)]
    [InlineData("POST", "/api/sessions/1/waive-fee", "MGR", WaiveBody)]
    public async Task RoleWithClaim_IsNot403(string method, string route, string role, string? body)
    {
        var response = await SendAsync(method, route, _factory.MintRoleToken(role), body);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            $"{role} holds the matrix claim for {method} {route}");
    }

    [Theory]
    [InlineData("GET", "/api/sessions/late-fees/preview", null)]
    [InlineData("POST", "/api/sessions/late-fees/batch", BatchBody)]
    [InlineData("POST", "/api/sessions/1/waive-fee", WaiveBody)]
    public async Task Wildcard_SatisfiesEveryFeeRoute(string method, string route, string? body)
    {
        var response = await SendAsync(method, route, _factory.MintWildcardToken(), body);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("GET", "/api/sessions/late-fees/preview")]
    [InlineData("POST", "/api/sessions/late-fees/batch")]
    [InlineData("POST", "/api/sessions/1/waive-fee")]
    public async Task Unauthenticated_Is401(string method, string route)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "POST")
        {
            request.Content = new StringContent(WaiveBody, Encoding.UTF8, "application/json");
        }

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
