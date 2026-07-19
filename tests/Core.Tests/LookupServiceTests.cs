using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Services;

namespace Core.Tests;

public class LookupServiceTests
{
    private readonly Mock<IRepository<AppointmentStatus>> _mockRepo;
    private readonly LookupService _service;

    public LookupServiceTests()
    {
        _mockRepo = new Mock<IRepository<AppointmentStatus>>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(sp => sp.GetService(typeof(IRepository<AppointmentStatus>)))
            .Returns(_mockRepo.Object);
        var fakeLogger = Mock.Of<ILogger<LookupService>>();
        _service = new LookupService(mockServiceProvider.Object, fakeLogger);
    }

    [Theory]
    [InlineData("appointment-statuses")]
    [InlineData("payment-types")]
    [InlineData("role-types")]
    [InlineData("specialty-types")]
    public void IsValidTableName_ReturnsTrue_ForValidNames(string tableName)
    {
        Assert.True(_service.IsValidTableName(tableName));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    [InlineData("users")]
    public void IsValidTableName_ReturnsFalse_ForInvalidNames(string tableName)
    {
        Assert.False(_service.IsValidTableName(tableName));
    }

    [Fact]
    public void Registry_ContainsAll4ExpectedTableNames()
    {
        Assert.True(_service.IsValidTableName("appointment-statuses"));
        Assert.True(_service.IsValidTableName("payment-types"));
        Assert.True(_service.IsValidTableName("role-types"));
        Assert.True(_service.IsValidTableName("specialty-types"));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsItems_ForValidTableName()
    {
        // Arrange
        var entities = new List<AppointmentStatus>
        {
            new() { Id = 1, Abbreviation = "PROP", Name = "Proposed", Description = "Requested", SortOrder = 1 },
            new() { Id = 2, Abbreviation = "CONF", Name = "Confirmed", Description = "Confirmed by patient", SortOrder = 2 }
        };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(entities);

        // Act
        var result = await _service.GetAllAsync("appointment-statuses");

        // Assert
        var items = result.ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(1, items[0].Id);
        Assert.Equal("PROP", items[0].Abbreviation);
        Assert.Equal("Proposed", items[0].Name);
        Assert.Equal("Requested", items[0].Description);
        Assert.Equal(1, items[0].SortOrder);
    }

    [Fact]
    public async Task GetAllAsync_ThrowsArgumentException_ForInvalidTableName()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetAllAsync("invalid"));
    }

    // --- WP-39 follow-up (owner ruling 2026-07-19): lookup GETs are ordered server-side ---

    [Fact]
    public async Task GetAllAsync_ReturnsItems_OrderedBySortOrderThenName()
    {
        // Arrange: deliberately out of order on both keys (repo returns arbitrary/ID order).
        var entities = new List<AppointmentStatus>
        {
            new() { Id = 1, Abbreviation = "ZZ", Name = "Zebra",    SortOrder = 2 },
            new() { Id = 2, Abbreviation = "BB", Name = "Bravo",    SortOrder = 1 },
            new() { Id = 3, Abbreviation = "AA", Name = "Alpha",    SortOrder = 2 },
            new() { Id = 4, Abbreviation = "MM", Name = "Mike",     SortOrder = 0 },
            new() { Id = 5, Abbreviation = "CC", Name = "Charlie",  SortOrder = 1 },
        };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(entities);

        // Act
        var items = (await _service.GetAllAsync("appointment-statuses")).ToList();

        // Assert: SortOrder ASC, then Name ASC within the same SortOrder.
        Assert.Equal(new[] { "Mike", "Bravo", "Charlie", "Alpha", "Zebra" },
            items.Select(i => i.Name).ToArray());
        Assert.Equal(new[] { 0, 1, 1, 2, 2 }, items.Select(i => i.SortOrder).ToArray());
    }

    [Fact]
    public async Task GetAllAsync_SpecialtyTypes_EnrichedProjectionIsOrderedToo()
    {
        UseSpecialtyRepo();
        _specialtyPriceRepo.Setup(r => r.GetAllWithPricesAsync()).ReturnsAsync(new List<SpecialtyType>
        {
            new() { Id = 1, Abbreviation = "TL", Name = "Language Therapy", SortOrder = 5,
                    DurationPrices = { Price(60, 45.00m, "2024-01-01") } },
            new() { Id = 2, Abbreviation = "FS", Name = "Physiotherapy",    SortOrder = 1 },
            new() { Id = 3, Abbreviation = "TO", Name = "Occupational",     SortOrder = 1 },
        });

        var items = (await _specialtyService.GetAllAsync("specialty-types")).ToList();

        Assert.Equal(new[] { "Occupational", "Physiotherapy", "Language Therapy" },
            items.Select(i => i.Name).ToArray());
        // The enrichment survives the ordering (last item carries its price sheet).
        Assert.Single(items[2].DurationPrices);
        Assert.Equal(60, items[2].DurationPrices[0].DurationMinutes);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsItem_WhenExists()
    {
        // Arrange
        var entity = new AppointmentStatus { Id = 1, Abbreviation = "PROP", Name = "Proposed", Description = "Desc", SortOrder = 0 };
        _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(entity);

        // Act
        var result = await _service.GetByIdAsync("appointment-statuses", 1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("PROP", result.Abbreviation);
        Assert.Equal("Proposed", result.Name);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((AppointmentStatus?)null);

        // Act
        var result = await _service.GetByIdAsync("appointment-statuses", 99);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_MapsRequestToEntityAndReturnsLookupItem()
    {
        // Arrange
        var request = new LookupCreateRequest
        {
            Abbreviation = "NEW",
            Name = "NewStatus",
            Description = "A new status",
            SortOrder = 5,
        };
        var created = new AppointmentStatus
        {
            Id = 10,
            Abbreviation = "NEW",
            Name = "NewStatus",
            Description = "A new status",
            SortOrder = 5,
        };
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<AppointmentStatus>())).ReturnsAsync(created);

        // Act
        var result = await _service.CreateAsync("appointment-statuses", request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.Id);
        Assert.Equal("NEW", result.Abbreviation);
        Assert.Equal("NewStatus", result.Name);
        Assert.Equal("A new status", result.Description);
        Assert.Equal(5, result.SortOrder);
    }

    [Fact]
    public async Task UpdateAsync_AppliesPartialUpdates_WhenEntityExists()
    {
        // Arrange
        var existing = new AppointmentStatus { Id = 1, Abbreviation = "OLD", Name = "OldName", Description = "OldDesc", SortOrder = 0 };
        var request = new LookupUpdateRequest { Name = "NewName", SortOrder = 3 };
        _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(existing);
        _mockRepo.Setup(r => r.UpdateAsync(It.IsAny<AppointmentStatus>())).Returns(Task.CompletedTask);

        // Act
        var result = await _service.UpdateAsync("appointment-statuses", 1, request);

        // Assert
        Assert.True(result);
        Assert.Equal("OLD", existing.Abbreviation); // unchanged
        Assert.Equal("NewName", existing.Name); // updated
        Assert.Equal("OldDesc", existing.Description); // unchanged
        Assert.Equal(3, existing.SortOrder); // updated
        _mockRepo.Verify(r => r.UpdateAsync(existing), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsFalse_WhenEntityNotExists()
    {
        // Arrange
        var request = new LookupUpdateRequest { Name = "New" };
        _mockRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((AppointmentStatus?)null);

        // Act
        var result = await _service.UpdateAsync("appointment-statuses", 99, request);

        // Assert
        Assert.False(result);
    }

    // --- WP-23 (F6): DefaultAmount is a per-type field — SpecialtyType only ---
    // WP-39: specialty-types READS now flow through ISpecialtyPriceRepository (a single
    // Include-join query so the projection can carry durationPrices without an N+1), so the
    // helper registers both repos; Create/Update still use the generic IRepository<SpecialtyType>.

    private Mock<IRepository<SpecialtyType>> UseSpecialtyRepo()
    {
        var mockSpecialtyRepo = new Mock<IRepository<SpecialtyType>>();
        _specialtyPriceRepo = new Mock<ISpecialtyPriceRepository>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(sp => sp.GetService(typeof(IRepository<SpecialtyType>)))
            .Returns(mockSpecialtyRepo.Object);
        mockServiceProvider
            .Setup(sp => sp.GetService(typeof(ISpecialtyPriceRepository)))
            .Returns(_specialtyPriceRepo.Object);
        _specialtyService = new LookupService(mockServiceProvider.Object, Mock.Of<ILogger<LookupService>>());
        return mockSpecialtyRepo;
    }

    private LookupService _specialtyService = null!;
    private Mock<ISpecialtyPriceRepository> _specialtyPriceRepo = null!;

    [Fact]
    public async Task GetAllAsync_SpecialtyTypes_PopulatesDefaultAmount()
    {
        UseSpecialtyRepo();
        _specialtyPriceRepo.Setup(r => r.GetAllWithPricesAsync()).ReturnsAsync(new List<SpecialtyType>
        {
            new() { Id = 1, Abbreviation = "TL", Name = "Language Therapy", DefaultAmount = 65.00m },
            new() { Id = 2, Abbreviation = "FS", Name = "Physiotherapy", DefaultAmount = null },
        });

        var items = (await _specialtyService.GetAllAsync("specialty-types")).ToList();

        Assert.Equal(65.00m, items[0].DefaultAmount);
        Assert.Null(items[1].DefaultAmount);
    }

    [Fact]
    public async Task GetAllAsync_NonSpecialtyTable_DefaultAmountIsAlwaysNull()
    {
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<AppointmentStatus>
        {
            new() { Id = 1, Abbreviation = "PROP", Name = "Proposed" },
        });

        var items = (await _service.GetAllAsync("appointment-statuses")).ToList();

        Assert.Null(items[0].DefaultAmount);
    }

    [Fact]
    public async Task CreateAsync_SpecialtyType_HonorsDefaultAmount()
    {
        var repo = UseSpecialtyRepo();
        SpecialtyType? captured = null;
        repo.Setup(r => r.AddAsync(It.IsAny<SpecialtyType>()))
            .Callback<SpecialtyType>(e => captured = e)
            .ReturnsAsync((SpecialtyType e) => e);

        var result = await _specialtyService.CreateAsync("specialty-types",
            new LookupCreateRequest { Abbreviation = "HYD", Name = "Hydrotherapy", DefaultAmount = 80.50m });

        Assert.Equal(80.50m, captured!.DefaultAmount);
        Assert.Equal(80.50m, result.DefaultAmount);
    }

    [Fact]
    public async Task CreateAsync_NonSpecialtyTable_IgnoresDefaultAmount()
    {
        AppointmentStatus? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<AppointmentStatus>()))
            .Callback<AppointmentStatus>(e => captured = e)
            .ReturnsAsync((AppointmentStatus e) => e);

        var result = await _service.CreateAsync("appointment-statuses",
            new LookupCreateRequest { Abbreviation = "NEW", Name = "NewStatus", DefaultAmount = 99m });

        Assert.NotNull(captured); // entity has no DefaultAmount property to set — nothing to assert on it
        Assert.Null(result.DefaultAmount);
    }

    [Fact]
    public async Task UpdateAsync_SpecialtyType_SetsDefaultAmount_AndNullMeansUnchanged()
    {
        var repo = UseSpecialtyRepo();
        var existing = new SpecialtyType { Id = 1, Abbreviation = "TL", Name = "Language Therapy", DefaultAmount = 65m };
        repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(existing);
        repo.Setup(r => r.UpdateAsync(It.IsAny<SpecialtyType>())).Returns(Task.CompletedTask);

        // null = unchanged
        await _specialtyService.UpdateAsync("specialty-types", 1, new LookupUpdateRequest { Name = "Renamed" });
        Assert.Equal(65m, existing.DefaultAmount);

        // provided = set
        await _specialtyService.UpdateAsync("specialty-types", 1, new LookupUpdateRequest { DefaultAmount = 70.25m });
        Assert.Equal(70.25m, existing.DefaultAmount);
    }

    // --- WP-39 (PR-1/PR-2): offeredOnSite + current-effective durationPrices projection ---

    private static SpecialtyDurationPrice Price(int duration, decimal amount, string effectiveFrom) =>
        new()
        {
            DurationMinutes = duration,
            Amount = amount,
            EffectiveFrom = Iso(effectiveFrom),
        };

    private static DateOnly Iso(string date) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task GetAllAsync_SpecialtyTypes_ProjectsCurrentEffectivePrices_AndOfferedOnSite()
    {
        UseSpecialtyRepo();
        _specialtyPriceRepo.Setup(r => r.GetAllWithPricesAsync()).ReturnsAsync(new List<SpecialtyType>
        {
            new()
            {
                Id = 1, Abbreviation = "TL", Name = "Language Therapy", OfferedOnSite = true,
                DurationPrices =
                {
                    // 30-min: two effective rows -> the LATEST effectiveFrom <= today wins.
                    Price(30, 22.00m, "2020-01-01"),
                    Price(30, 25.00m, "2025-06-01"),
                    // 60-min: one past row + one FUTURE row -> future excluded, past wins.
                    Price(60, 45.00m, "2024-01-01"),
                    Price(60, 99.00m, "2999-01-01"),
                    // 90-min: only future -> duration ABSENT from the projection.
                    Price(90, 80.00m, "2999-01-01"),
                    // 45/120: no rows -> absent.
                },
            },
        });

        var item = (await _specialtyService.GetAllAsync("specialty-types")).Single();

        Assert.True(item.OfferedOnSite);
        Assert.Equal(2, item.DurationPrices.Count);
        var p30 = item.DurationPrices[0];
        Assert.Equal(30, p30.DurationMinutes);
        Assert.Equal(25.00m, p30.Amount);
        Assert.Equal(Iso("2025-06-01"), p30.EffectiveFrom);
        var p60 = item.DurationPrices[1];
        Assert.Equal(60, p60.DurationMinutes);
        Assert.Equal(45.00m, p60.Amount);
        // Single repository call carried everything — no per-item follow-up queries (WP-29 rule).
        _specialtyPriceRepo.Verify(r => r.GetAllWithPricesAsync(), Times.Once);
        _specialtyPriceRepo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetByIdAsync_SpecialtyType_ProjectsPricesFromSingleQuery()
    {
        UseSpecialtyRepo();
        _specialtyPriceRepo.Setup(r => r.GetWithPricesAsync(7)).ReturnsAsync(
            new SpecialtyType
            {
                Id = 7, Abbreviation = "HYD", Name = "Hydrotherapy", OfferedOnSite = false,
                DurationPrices = { Price(45, 35.00m, "2024-05-01") },
            });

        var item = await _specialtyService.GetByIdAsync("specialty-types", 7);

        Assert.NotNull(item);
        Assert.False(item.OfferedOnSite);
        var p = Assert.Single(item.DurationPrices);
        Assert.Equal(45, p.DurationMinutes);
        Assert.Equal(35.00m, p.Amount);
        _specialtyPriceRepo.Verify(r => r.GetWithPricesAsync(7), Times.Once);
        _specialtyPriceRepo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAllAsync_NonSpecialtyTable_OfferedOnSiteFalse_AndNoPrices()
    {
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<AppointmentStatus>
        {
            new() { Id = 1, Abbreviation = "PROP", Name = "Proposed" },
        });

        var items = (await _service.GetAllAsync("appointment-statuses")).ToList();

        Assert.False(items[0].OfferedOnSite);
        Assert.Empty(items[0].DurationPrices);
    }

    [Fact]
    public async Task CreateAsync_SpecialtyType_HonorsOfferedOnSite_AndDefaultsFalse()
    {
        var repo = UseSpecialtyRepo();
        SpecialtyType? captured = null;
        repo.Setup(r => r.AddAsync(It.IsAny<SpecialtyType>()))
            .Callback<SpecialtyType>(e => captured = e)
            .ReturnsAsync((SpecialtyType e) => e);

        await _specialtyService.CreateAsync("specialty-types",
            new LookupCreateRequest { Abbreviation = "DOM", Name = "Home Therapy", OfferedOnSite = true });
        Assert.True(captured!.OfferedOnSite);

        await _specialtyService.CreateAsync("specialty-types",
            new LookupCreateRequest { Abbreviation = "CLN", Name = "Clinic-only Therapy" });
        Assert.False(captured!.OfferedOnSite); // omitted = false (DB default)
    }

    [Fact]
    public async Task UpdateAsync_SpecialtyType_SetsOfferedOnSite_AndNullMeansUnchanged()
    {
        var repo = UseSpecialtyRepo();
        var existing = new SpecialtyType { Id = 1, Abbreviation = "TL", Name = "Language Therapy", OfferedOnSite = true };
        repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(existing);
        repo.Setup(r => r.UpdateAsync(It.IsAny<SpecialtyType>())).Returns(Task.CompletedTask);

        // null = unchanged
        await _specialtyService.UpdateAsync("specialty-types", 1, new LookupUpdateRequest { Name = "Renamed" });
        Assert.True(existing.OfferedOnSite);

        // provided = set (false is a real value, not a sentinel)
        await _specialtyService.UpdateAsync("specialty-types", 1, new LookupUpdateRequest { OfferedOnSite = false });
        Assert.False(existing.OfferedOnSite);
    }

    [Fact]
    public async Task CreateAsync_NonSpecialtyTable_IgnoresOfferedOnSite()
    {
        AppointmentStatus? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<AppointmentStatus>()))
            .Callback<AppointmentStatus>(e => captured = e)
            .ReturnsAsync((AppointmentStatus e) => e);

        var result = await _service.CreateAsync("appointment-statuses",
            new LookupCreateRequest { Abbreviation = "NEW", Name = "NewStatus", OfferedOnSite = true });

        Assert.NotNull(captured); // entity has no OfferedOnSite property — nothing to set
        Assert.False(result.OfferedOnSite);
    }
}
