using Microsoft.EntityFrameworkCore;
using Moq;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;
using Xunit;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// WP-39: the price-sheet reads load specialties WITH their DurationPrices in ONE repository
/// call (Include join — WP-29 no-N+1 rule); appends are append-only and audit-stamped by the
/// DbContext (LastUpdatedByUserId via ICurrentUserService — mocked here, the house pattern).
/// </summary>
public class SpecialtyPriceRepositoryTests
{
    private static DbContextOptions<ApplicationDbContext> CreateInMemoryOptions(string dbName)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
    }

    private static SpecialtyDurationPrice Row(int duration, decimal amount, DateOnly effectiveFrom) =>
        new() { DurationMinutes = duration, Amount = amount, EffectiveFrom = effectiveFrom };

    private static async Task SeedAsync(DbContextOptions<ApplicationDbContext> options)
    {
        using var context = new ApplicationDbContext(options);
        var speech = new SpecialtyType { Abbreviation = "TL", Name = "Language Therapy" };
        speech.DurationPrices.Add(Row(30, 25.00m, new DateOnly(2025, 6, 1)));
        speech.DurationPrices.Add(Row(30, 22.00m, new DateOnly(2020, 1, 1)));
        speech.DurationPrices.Add(Row(60, 45.00m, new DateOnly(2024, 1, 1)));
        var physio = new SpecialtyType { Abbreviation = "FS", Name = "Physiotherapy" };
        context.SpecialtyTypes.AddRange(speech, physio);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAllWithPricesAsync_SingleCall_ReturnsSpecialtiesWithFullPriceHistory()
    {
        var options = CreateInMemoryOptions("SpecialtyPriceRepo_GetAll");
        await SeedAsync(options);

        using var context = new ApplicationDbContext(options);
        var repository = new SpecialtyPriceRepository(context);

        var specialties = await repository.GetAllWithPricesAsync();

        Assert.Equal(2, specialties.Count);
        var speech = specialties.Single(s => s.Abbreviation == "TL");
        Assert.Equal(3, speech.DurationPrices.Count); // full history hydrated in the same call
        var physio = specialties.Single(s => s.Abbreviation == "FS");
        Assert.Empty(physio.DurationPrices);
    }

    [Fact]
    public async Task GetWithPricesAsync_ReturnsSpecialtyWithPrices_OrNullWhenUnknown()
    {
        var options = CreateInMemoryOptions("SpecialtyPriceRepo_GetOne");
        await SeedAsync(options);

        using var context = new ApplicationDbContext(options);
        var repository = new SpecialtyPriceRepository(context);
        var speechId = context.SpecialtyTypes.Single(s => s.Abbreviation == "TL").Id;

        var speech = await repository.GetWithPricesAsync(speechId);
        Assert.NotNull(speech);
        Assert.Equal(3, speech.DurationPrices.Count);

        Assert.Null(await repository.GetWithPricesAsync(99999));
    }

    [Fact]
    public async Task AddRangeAsync_AppendsRows_WithoutTouchingExistingHistory_AndStampsAudit()
    {
        var options = CreateInMemoryOptions("SpecialtyPriceRepo_Append");
        await SeedAsync(options);

        // House gotcha: audit columns are stamped in ApplicationDbContext.SaveChangesAsync via
        // the (optional) ICurrentUserService — supply a mock so LastUpdatedByUserId is real.
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(u => u.UserId).Returns(42);
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);

        int speechId;
        using (var context = new ApplicationDbContext(options, currentUser.Object))
        {
            var repository = new SpecialtyPriceRepository(context);
            speechId = context.SpecialtyTypes.Single(s => s.Abbreviation == "TL").Id;

            await repository.AddRangeAsync(
            [
                new SpecialtyDurationPrice
                {
                    SpecialtyTypeId = speechId,
                    DurationMinutes = 90,
                    Amount = 80.00m,
                    EffectiveFrom = new DateOnly(2026, 8, 1),
                },
            ]);
        }

        using (var context = new ApplicationDbContext(options))
        {
            var rows = context.SpecialtyDurationPrices
                .Where(r => r.SpecialtyTypeId == speechId)
                .ToList();
            Assert.Equal(4, rows.Count); // 3 seeded + 1 appended; nothing replaced
            var appended = rows.Single(r => r.DurationMinutes == 90);
            Assert.Equal(80.00m, appended.Amount);
            Assert.Equal(new DateOnly(2026, 8, 1), appended.EffectiveFrom);
            Assert.Equal(42, appended.LastUpdatedByUserId);        // stamped from ICurrentUserService
            Assert.NotEqual(default, appended.CreatedTimestamp);   // stamped on Add
        }
    }
}
