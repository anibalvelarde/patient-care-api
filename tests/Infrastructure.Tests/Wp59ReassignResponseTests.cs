using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;
using Xunit;

namespace Infrastructure.Tests;

/// <summary>
/// Found in the WP-59 local E2E: <c>PUT /api/sessions/{id}</c> with a new <c>therapistId</c> SAVED
/// the reassignment and then 500'd (NullReferenceException in <c>ExtractSessionEvent</c>). Changing
/// the FK to a principal the context hasn't loaded makes EF null the <c>Therapist</c> navigation,
/// and the response mapper dereferences it. Same trap for a status change (the status name fell
/// back to "Completed"). The repository now reloads changed references before mapping.
/// </summary>
public class Wp59ReassignResponseTests
{
    private static async Task<DbContextOptions<ApplicationDbContext>> SeedAsync(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options;
        using var ctx = new ApplicationDbContext(options);
        ctx.AppointmentStatuses.AddRange(
            new AppointmentStatus { Id = 2, Name = "Confirmed", Description = "" },
            new AppointmentStatus { Id = 6, Name = "CheckedIn", Description = "" });
        ctx.Set<Therapist>().AddRange(
            new Therapist { Id = 1, User = new User { Id = 11, FirstName = "Ana", LastName = "Synthetic" } },
            new Therapist { Id = 2, User = new User { Id = 12, FirstName = "Ben", LastName = "Synthetic" } });
        ctx.TherapySessions.Add(new TherapySession
        {
            Id = 2, TherapistId = 1, AppointmentStatusId = 2,
            Patient = new Patient { Id = 2, User = new User { Id = 22, FirstName = "Pat", LastName = "Synthetic" } },
            SessionDate = DateOnly.Parse("2026-11-23"), SessionTime = TimeOnly.Parse("10:30"), Duration = 30,
            Amount = 50m, Notes = "", TherapyTypes = "TL",
        });
        await ctx.SaveChangesAsync();
        return options;
    }

    [Fact]
    public async Task Reassign_ToATherapistNotYetLoaded_ReturnsTheNewTherapist_NoCrash()
    {
        var options = await SeedAsync(nameof(Reassign_ToATherapistNotYetLoaded_ReturnsTheNewTherapist_NoCrash));
        using var ctx = new ApplicationDbContext(options);

        var result = await new SessionEventRepository(ctx).UpdateAsync(2, new SessionEventUpdateRequest
        {
            SessionTime = TimeOnly.Parse("10:30"), Amount = 50m, Discount = 0m, TherapistId = 2, Notes = "",
        });

        Assert.Equal(2, result.TherapistId);
        Assert.Contains("Ben", result.Therapist);
    }

    [Fact]
    public async Task StatusChange_ReturnsTheNewStatusName()
    {
        var options = await SeedAsync(nameof(StatusChange_ReturnsTheNewStatusName));
        using var ctx = new ApplicationDbContext(options);

        var result = await new SessionEventRepository(ctx).UpdateAsync(2, new SessionEventUpdateRequest
        {
            SessionTime = TimeOnly.Parse("10:30"), Amount = 50m, Discount = 0m,
            AppointmentStatusId = SessionStatus.CheckedIn, Notes = "",
        });

        Assert.Equal("CheckedIn", result.StatusName);
    }
}
