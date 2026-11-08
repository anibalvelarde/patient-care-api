using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;
using Xunit;

namespace Infrastructure.Tests;

/// <summary>WP-59: the double-booking guard's candidate query against EF (InMemory).</summary>
public class Wp59SlotCandidatesTests
{
    private static readonly DateOnly Day = DateOnly.Parse("2026-11-09");

    private static TherapySession Row(int id, int therapistId, int patientId, DateOnly date, int status) => new()
    {
        Id = id, TherapistId = therapistId, PatientId = patientId, SessionDate = date,
        SessionTime = TimeOnly.Parse("10:00"), Duration = 30, AppointmentStatusId = status,
        Notes = "", TherapyTypes = "TL",
    };

    [Fact]
    public async Task ReturnsTherapistAndPatientRows_ThatDay_ExcludingCancelledAndSelf()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(ReturnsTherapistAndPatientRows_ThatDay_ExcludingCancelledAndSelf)).Options;
        using (var ctx = new ApplicationDbContext(options))
        {
            ctx.Set<Therapist>().Add(new Therapist { Id = 3, User = new User { Id = 3, FirstName = "Ana", LastName = "Synthetic" } });
            ctx.Set<Therapist>().Add(new Therapist { Id = 9, User = new User { Id = 9, FirstName = "Ben", LastName = "Synthetic" } });
            ctx.Set<Therapist>().Add(new Therapist { Id = 8, User = new User { Id = 8, FirstName = "Cy", LastName = "Synthetic" } });
            ctx.TherapySessions.AddRange(
                Row(1, therapistId: 3, patientId: 10, Day, SessionStatus.Confirmed),   // therapist ✓
                Row(2, therapistId: 9, patientId: 922, Day, SessionStatus.NoShow),     // patient ✓ (NoShow occupies)
                Row(3, therapistId: 3, patientId: 11, Day, SessionStatus.Cancelled),   // cancelled ✗
                Row(4, therapistId: 3, patientId: 922, Day, SessionStatus.Proposed),   // self ✗
                Row(5, therapistId: 3, patientId: 12, Day.AddDays(1), SessionStatus.Confirmed), // other day ✗
                Row(6, therapistId: 8, patientId: 13, Day, SessionStatus.Confirmed));  // unrelated ✗
            await ctx.SaveChangesAsync();
        }

        using var read = new ApplicationDbContext(options);
        var rows = await new TherapySessionRepository(read).GetSlotCandidatesAsync(Day, therapistId: 3, patientId: 922, excludeSessionId: 4);

        Assert.Equal(new[] { 1, 2 }, rows.Select(r => r.Id).OrderBy(i => i).ToArray());
        Assert.Equal("Ana", rows.Single(r => r.Id == 1).Therapist?.User?.FirstName);
    }
}
