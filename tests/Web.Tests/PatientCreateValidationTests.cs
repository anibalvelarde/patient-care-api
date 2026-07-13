using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Neurocorp.Api.Web.Tests.Authorization;

namespace Neurocorp.Api.Web.Tests;

/// <summary>
/// B1 regression (intake 2026-07-07-001): the UI's Gender "Other" reached the DB unvalidated and
/// blew up as MySQL error 1265 → an opaque 500. The API contract is now: Gender must be one of
/// Male/Female/Other — anything else is a 400 at the model-validation layer, and "Other" is a
/// first-class accepted value (DB enum widened by V024).
/// Runs through the real host pipeline so [ApiController] model validation genuinely executes.
/// </summary>
public class PatientCreateValidationTests : IClassFixture<AccessControlTestFactory>
{
    private readonly AccessControlTestFactory _factory;

    public PatientCreateValidationTests(AccessControlTestFactory factory) => _factory = factory;

    private HttpClient CreateMgrClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.MintRoleToken("MGR"));
        return client;
    }

    private static object CreateBody(string? gender, string suffix, string? cedula) => new
    {
        firstName = "B1",
        lastName = $"Regression-{suffix}",
        middleName = "",
        medicalRecordNumber = $"B1-TEST-{suffix}",
        cedula,
        dateOfBirth = "2015-06-01",
        email = $"b1-{suffix}@neurocorp.test",
        phoneNumber = "555-0100",
        gender,
    };

    // WP-25 (F5): cedula is required at create, and uq_patient_cedula is UNIQUE — every create
    // body gets its own value (a shared constant would 409 the second successful create).
    private static object CreateBody(string? gender, string suffix) =>
        CreateBody(gender, suffix, $"WP25-{suffix}-{Guid.NewGuid().ToString("N")[..8]}");

    [Theory]
    [InlineData("Nonbinary")]
    [InlineData("F")]        // the pre-B1 test shorthand — never a valid DB value
    [InlineData("other")]    // enum values are case-sensitive at the contract level
    public async Task Post_WithInvalidGender_Returns400(string invalidGender)
    {
        var client = CreateMgrClient();

        var response = await client.PostAsJsonAsync("/api/patients", CreateBody(invalidGender, $"inv-{invalidGender.Length}"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Gender");
    }

    [Fact]
    public async Task Post_WithMissingGender_Returns400()
    {
        var client = CreateMgrClient();

        var response = await client.PostAsJsonAsync("/api/patients", CreateBody(null, "missing"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("Male", "male")]
    [InlineData("Female", "female")]
    [InlineData("Other", "other-ok")]   // the B1 trigger value — must create, not 500
    public async Task Post_WithValidGender_Creates(string gender, string suffix)
    {
        var client = CreateMgrClient();

        var response = await client.PostAsJsonAsync("/api/patients", CreateBody(gender, suffix));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // WP-25 (F5, Questionnaire D): "Cedula | Passport" is mandatory at create. Missing (null) or
    // blank/whitespace must be a 400 at the model-validation layer — never a silent NULL insert.
    [Fact]
    public async Task Post_WithMissingCedula_Returns400()
    {
        var client = CreateMgrClient();

        var response = await client.PostAsJsonAsync("/api/patients", CreateBody("Male", "ced-missing", cedula: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Cedula");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_WithBlankCedula_Returns400(string blankCedula)
    {
        var client = CreateMgrClient();

        var response = await client.PostAsJsonAsync("/api/patients", CreateBody("Male", $"ced-blank-{blankCedula.Length}", blankCedula));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Cedula");
    }

    [Fact]
    public async Task Put_WithInvalidGender_Returns400()
    {
        var client = CreateMgrClient();

        // Model validation rejects the body before the action runs — no seeded patient needed.
        var response = await client.PutAsJsonAsync("/api/patients/999999", new { gender = "Nonbinary" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Gender");
    }
}
