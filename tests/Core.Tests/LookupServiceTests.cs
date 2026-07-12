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

    private Mock<IRepository<SpecialtyType>> UseSpecialtyRepo()
    {
        var mockSpecialtyRepo = new Mock<IRepository<SpecialtyType>>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(sp => sp.GetService(typeof(IRepository<SpecialtyType>)))
            .Returns(mockSpecialtyRepo.Object);
        _specialtyService = new LookupService(mockServiceProvider.Object, Mock.Of<ILogger<LookupService>>());
        return mockSpecialtyRepo;
    }

    private LookupService _specialtyService = null!;

    [Fact]
    public async Task GetAllAsync_SpecialtyTypes_PopulatesDefaultAmount()
    {
        var repo = UseSpecialtyRepo();
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<SpecialtyType>
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
}
