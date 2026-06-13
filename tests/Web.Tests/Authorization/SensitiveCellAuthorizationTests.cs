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
///   Payments.Adjust       [AM,MGR]      PUT  /api/payments/{id}
///
/// WP-17B-2 bulk pass — additional cells whose granted set excludes a granular role:
///   Therapists.Edit       [AM,MGR]      POST /api/therapists, PUT /api/therapists/{id} (FD denied)
///   Patients.Delinquent.View [AM,MGR]   GET  /api/sessions/pastdue (D-4; FD denied)
/// and representative checks that the broad AM/FD/MGR decorations actually enforce:
///   Patients.View         [AM,FD,MGR]   GET  /api/patients (restricted role → 403; FD allowed)
///   Appointments.View     [AM,FD,MGR]   GET  /api/sessions/patient/{id}/discovery (D-5; FD allowed)
///
/// WP-17 D-6 — treatment-plan setup (Book) vs content edit (Edit):
///   TreatmentPlans.Edit   [AM,MGR]      PUT  /api/treatment-plans/{id} (content edit; FD denied)
///   TreatmentPlans.Book   [AM,FD,MGR]   POST /api/treatment-plans, POST …/{id}/schedule,
///                                       PUT …/{id}/activate|complete|cancel (FD retains setup)
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
    // Payments.Adjust [AM,MGR] — FD can Record but not adjust an existing payment.
    [InlineData("PUT", "/api/payments/1", "FD")]
    // Therapists.Edit [AM,MGR] — FD can view therapists but not create/edit them.
    [InlineData("POST", "/api/therapists", "FD")]
    [InlineData("PUT", "/api/therapists/1", "FD")]
    // Patients.Delinquent.View [AM,MGR] (D-4) — past-due session feed hidden from FD.
    [InlineData("GET", "/api/sessions/pastdue", "FD")]
    // Broad AM/FD/MGR decoration still locks out restricted roles entirely (no granular claims).
    [InlineData("GET", "/api/patients", "CARETAKER")]
    // TreatmentPlans.Edit [AM,MGR] (D-6) — editing an existing plan's content is denied to FD.
    [InlineData("PUT", "/api/treatment-plans/1", "FD")]
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
    // Payments.Adjust [AM,MGR].
    [InlineData("PUT", "/api/payments/1", "AM")]
    [InlineData("PUT", "/api/payments/1", "MGR")]
    // Therapists.Edit [AM,MGR] — AM and MGR can create/edit.
    [InlineData("POST", "/api/therapists", "AM")]
    [InlineData("PUT", "/api/therapists/1", "MGR")]
    // Patients.Delinquent.View [AM,MGR] (D-4) — past-due session feed visible to AM and MGR.
    [InlineData("GET", "/api/sessions/pastdue", "AM")]
    [InlineData("GET", "/api/sessions/pastdue", "MGR")]
    // Broad AM/FD/MGR decorations let FD through (Patients.View; Appointments.View for D-5 discovery).
    [InlineData("GET", "/api/patients", "FD")]
    [InlineData("GET", "/api/sessions/patient/1/discovery", "FD")]
    // TreatmentPlans.Edit [AM,MGR] (D-6) — AM and MGR may edit plan content.
    [InlineData("PUT", "/api/treatment-plans/1", "AM")]
    [InlineData("PUT", "/api/treatment-plans/1", "MGR")]
    // TreatmentPlans.Book [AM,FD,MGR] (D-6) — FD retains plan SETUP: create, schedule, lifecycle.
    [InlineData("POST", "/api/treatment-plans", "FD")]
    [InlineData("POST", "/api/treatment-plans/1/schedule", "FD")]
    [InlineData("PUT", "/api/treatment-plans/1/activate", "FD")]
    [InlineData("PUT", "/api/treatment-plans/1/cancel", "MGR")]
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

    // ── D-10: per-type lookup management (imperative authorization) ──────────────────
    // LookupsController.Create/Update authorize against Admin.Lookups.<Type>.Manage chosen from the
    // {tableName} segment. All four Manage claims are SYSADMIN-only today, so only the wildcard token
    // (or a token explicitly carrying the matching per-type claim) gets through — and crucially, a
    // token holding ONE type's Manage claim must NOT be able to manage a DIFFERENT type.

    [Theory]
    [InlineData("POST", "/api/lookups/payment-types")]
    [InlineData("PUT", "/api/lookups/payment-types/1")]
    public async Task Lookups_WithMatchingTypeClaim_IsNotBlocked(string method, string route)
    {
        var token = _factory.MintWithPermissions("Admin.Lookups.PaymentType.Manage");

        var response = await SendAsync(method, route, token);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "a token carrying Admin.Lookups.PaymentType.Manage may manage payment-types");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, "the token is valid");
    }

    [Theory]
    [InlineData("POST", "/api/lookups/role-types")]
    [InlineData("PUT", "/api/lookups/role-types/1")]
    [InlineData("POST", "/api/lookups/specialty-types")]
    public async Task Lookups_WithWrongTypeClaim_Is403(string method, string route)
    {
        // Holds PaymentType.Manage only — must not leak into other lookup types.
        var token = _factory.MintWithPermissions("Admin.Lookups.PaymentType.Manage");

        var response = await SendAsync(method, route, token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the per-type claim for one lookup type must not authorize managing another type");
    }

    [Theory]
    // A normal granular role holds the *.View lookup claims but no *.Manage claim ⇒ denied.
    [InlineData("POST", "/api/lookups/payment-types", "MGR")]
    [InlineData("POST", "/api/lookups/role-types", "AM")]
    public async Task Lookups_Manage_NormalRole_Is403(string method, string route, string role)
    {
        var response = await SendAsync(method, route, _factory.MintRoleToken(role));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"{role} has no Admin.Lookups.*.Manage claim (those are SYSADMIN-only today)");
    }

    [Theory]
    [InlineData("POST", "/api/lookups/role-types")]
    [InlineData("PUT", "/api/lookups/payment-types/1")]
    public async Task Lookups_Manage_Sysadmin_IsNotBlocked(string method, string route)
    {
        var response = await SendAsync(method, route, _factory.MintWildcardToken());

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "SYSADMIN passes every Manage claim via the wildcard");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, "the token is valid");
    }

    [Fact]
    public async Task Lookups_Manage_NoToken_Is401()
    {
        var response = await SendAsync("POST", "/api/lookups/payment-types", token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the secure-by-default fallback policy still requires authentication before the action runs");
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
