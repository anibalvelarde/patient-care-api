using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Web.Tests.Authorization;

namespace Neurocorp.Api.Web.Tests;

/// <summary>
/// WP-59 (F-2): the double-booking 409 through the real pipeline (JWT → policy → controller →
/// service → GlobalExceptionHandler), so the wire shape the UI reads is what is asserted:
/// a 409 ProblemDetails whose <c>detail</c> names the clashing session. Uses the reinstate path
/// (check-in of a Cancelled session) — it needs only two seeded rows.
/// </summary>
public class Wp59DoubleBookingHttpTests : IClassFixture<AccessControlTestFactory>
{
    private readonly AccessControlTestFactory _factory;

    public Wp59DoubleBookingHttpTests(AccessControlTestFactory factory) => _factory = factory;

    private async Task SeedAsync(int cancelledId, int busyId, bool slotTaken)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var day = DateOnly.Parse("2026-11-09");
        // The session repository joins patient + therapist (required navigations), so seed them once.
        if (!db.Set<Therapist>().Any(t => t.Id == 59003))
        {
            db.Set<Therapist>().Add(new Therapist { Id = 59003, User = new User { Id = 59003, FirstName = "Ana", LastName = "Synthetic" } });
            db.Set<Patient>().Add(new Patient { Id = 59922, User = new User { Id = 59922, FirstName = "Pat", LastName = "Synthetic" } });
            db.Set<Patient>().Add(new Patient { Id = 59500, User = new User { Id = 59500, FirstName = "Sam", LastName = "Synthetic" } });
        }
        db.TherapySessions.Add(new TherapySession
        {
            Id = cancelledId, PatientId = 59922, TherapistId = 59003, SessionDate = day,
            SessionTime = TimeOnly.Parse("10:00"), Duration = 30,
            AppointmentStatusId = SessionStatus.Cancelled, Notes = "", TherapyTypes = "TL",
        });
        db.TherapySessions.Add(new TherapySession
        {
            Id = busyId, PatientId = 59500, TherapistId = 59003, SessionDate = day,
            SessionTime = TimeOnly.Parse(slotTaken ? "10:00" : "10:30"), Duration = 30,
            AppointmentStatusId = SessionStatus.Proposed, Notes = "", TherapyTypes = "TL",
        });
        await db.SaveChangesAsync();
    }

    private HttpClient MgrClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.MintRoleToken("MGR"));
        return client;
    }

    [Fact]
    public async Task CheckIn_OfACancelledSession_IntoATakenSlot_Returns409ProblemDetails()
    {
        await SeedAsync(cancelledId: 59101, busyId: 59102, slotTaken: true);

        var response = await MgrClient().PutAsync("/api/sessions/59101/checkin", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(409);
        doc.RootElement.GetProperty("detail").GetString().Should()
            .Contain("already has a session on 2026-11-09 from 10:00 to 10:30 (session #59102)");
    }

    [Fact]
    public async Task CheckIn_OfACancelledSession_BackToBack_IsNotAConflict()
    {
        await SeedAsync(cancelledId: 59201, busyId: 59202, slotTaken: false);

        var response = await MgrClient().PutAsync("/api/sessions/59201/checkin", content: null);

        response.StatusCode.Should().NotBe(HttpStatusCode.Conflict);
    }
}
