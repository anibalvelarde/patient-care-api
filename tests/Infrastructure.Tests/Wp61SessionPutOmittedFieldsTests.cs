using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;
using Xunit;

namespace Infrastructure.Tests;

/// <summary>
/// WP-61 (found in the WP-60 local E2E): <c>PUT /api/sessions/{id}</c> without <c>therapyType</c>
/// blanked the stored TherapyTypes — the mapper assigned it unconditionally and the DTO defaulted it
/// to "". Same hazard for <c>notes</c> (defaulted to "") and <c>discount</c> (non-nullable, so an
/// omitted discount wrote 0). Omitted now means "keep what's stored", like Duration/Amount.
/// </summary>
public class Wp61SessionPutOmittedFieldsTests
{
    private static async Task<DbContextOptions<ApplicationDbContext>> SeedAsync(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options;
        using var ctx = new ApplicationDbContext(options);
        ctx.AppointmentStatuses.Add(new AppointmentStatus { Id = 2, Name = "Confirmed", Description = "" });
        ctx.Set<Therapist>().Add(new Therapist { Id = 1, User = new User { Id = 11, FirstName = "Ana", LastName = "Synthetic" } });
        ctx.TherapySessions.Add(new TherapySession
        {
            Id = 2, TherapistId = 1, AppointmentStatusId = 2,
            Patient = new Patient { Id = 2, User = new User { Id = 22, FirstName = "Pat", LastName = "Synthetic" } },
            SessionDate = DateOnly.Parse("2026-11-23"), SessionTime = TimeOnly.Parse("10:30"), Duration = 60,
            Amount = 70m, DiscountAmount = 14m, Notes = "stored note", TherapyTypes = "TL",
        });
        await ctx.SaveChangesAsync();
        return options;
    }

    private static async Task<TherapySession> ReloadAsync(DbContextOptions<ApplicationDbContext> options)
    {
        using var ctx = new ApplicationDbContext(options);
        return await ctx.TherapySessions.AsNoTracking().SingleAsync(s => s.Id == 2);
    }

    [Fact]
    public async Task Put_WithoutTherapyTypeNotesOrDiscount_KeepsAllThree()
    {
        var options = await SeedAsync(nameof(Put_WithoutTherapyTypeNotesOrDiscount_KeepsAllThree));
        using (var ctx = new ApplicationDbContext(options))
        {
            // A status-only edit: none of therapyType / notes / discount is sent.
            await new SessionEventRepository(ctx).UpdateAsync(2, new SessionEventUpdateRequest
            {
                SessionTime = TimeOnly.Parse("10:30"),
            });
        }

        var stored = await ReloadAsync(options);
        Assert.Equal("TL", stored.TherapyTypes);
        Assert.Equal("stored note", stored.Notes);
        Assert.Equal(14m, stored.DiscountAmount);
    }

    [Fact]
    public async Task Put_WithValues_StillAppliesThem()
    {
        var options = await SeedAsync(nameof(Put_WithValues_StillAppliesThem));
        using (var ctx = new ApplicationDbContext(options))
        {
            await new SessionEventRepository(ctx).UpdateAsync(2, new SessionEventUpdateRequest
            {
                SessionTime = TimeOnly.Parse("10:30"), TherapyType = "TO", Notes = "", Discount = 0m,
            });
        }

        var stored = await ReloadAsync(options);
        Assert.Equal("TO", stored.TherapyTypes);
        Assert.Equal("", stored.Notes);          // an explicit empty string still clears the notes
        Assert.Equal(0m, stored.DiscountAmount);
    }
}
