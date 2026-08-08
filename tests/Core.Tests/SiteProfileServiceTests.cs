using Neurocorp.Api.Core.Interfaces.Repositories;
using Moq;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.Sites;
using Neurocorp.Api.Core.Entities;
using Microsoft.Extensions.Logging;

namespace Core.Tests;

public class SiteProfileServiceTests
{
    [Fact]
    public void GoodConstructorTest()
    {
        // arrange
        var fakeRepo = Mock.Of<ISiteRepository>();
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();

        // act
        var svc = new SiteProfileService(fakeLogger, fakeRepo);

        // assert
        Assert.IsType<SiteProfileService>(svc);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsSiteProfile_WhenSiteExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        int testId = 1;
        var expectedSite = new Site
        {
            Id = testId,
            SiteName = "Main Clinic",
            RUC = "12345",
            InceptionDate = new DateTime(2020, 1, 1),
            Address = "123 Main St",
            Latitude = 10.5m,
            Longitude = -84.3m
        };
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync(expectedSite);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.GetByIdAsync(testId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedSite.Id, result.SiteId);
        Assert.Equal(expectedSite.SiteName, result.SiteName);
        Assert.Equal(expectedSite.Address, result.Address);
        Assert.Equal(expectedSite.Latitude, result.Latitude);
        Assert.Equal(expectedSite.Longitude, result.Longitude);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenSiteDoesNotExist()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        int testId = 99;
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync((Site?)null);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.GetByIdAsync(testId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsSiteProfiles()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        var sites = new List<Site>
        {
            new() { Id = 1, SiteName = "Clinic A" },
            new() { Id = 2, SiteName = "Clinic B" }
        };
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetAllAsync()).ReturnsAsync(sites);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.GetAllAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<SiteProfile>>(result);
        Assert.Equal(2, result.Count());
    }

    [Fact]
    public async Task CreateAsync_ReturnsCreatedSiteProfile()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        var request = new SiteProfileRequest
        {
            SiteName = "New Clinic",
            RUC = "99999",
            InceptionDate = new DateTime(2025, 6, 1),
            Address = "456 Oak Ave",
            Latitude = 11.0m,
            Longitude = -85.0m
        };
        var createdEntity = new Site
        {
            Id = 10,
            SiteName = request.SiteName,
            RUC = request.RUC,
            InceptionDate = request.InceptionDate,
            Address = request.Address,
            Latitude = request.Latitude,
            Longitude = request.Longitude
        };
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.AddAsync(It.IsAny<Site>())).ReturnsAsync(createdEntity);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.CreateAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(createdEntity.Id, result.SiteId);
        Assert.Equal(request.SiteName, result.SiteName);
        Assert.Equal(request.Address, result.Address);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsTrue_WhenSiteExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        int testId = 1;
        var existingSite = new Site
        {
            Id = testId,
            SiteName = "Old Name",
            Address = "Old Address"
        };
        var updateRequest = new SiteProfileUpdateRequest
        {
            SiteName = "New Name",
            Address = "New Address"
        };
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync(existingSite);
        _mockRepository.Setup(repo => repo.UpdateAsync(It.IsAny<Site>())).Returns(Task.CompletedTask);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.UpdateAsync(testId, updateRequest);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsFalse_WhenSiteDoesNotExist()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        int testId = 99;
        var updateRequest = new SiteProfileUpdateRequest
        {
            SiteName = "New Name"
        };
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync((Site?)null);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.UpdateAsync(testId, updateRequest);

        // Assert
        Assert.False(result);
    }

    // --- WP-32 (U4): idle auto-logoff field ---

    [Fact]
    public async Task GetByIdAsync_MapsIdleLogoffMinutes()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        int testId = 1;
        var site = new Site { Id = testId, SiteName = "Main Clinic", IdleLogoffMinutes = 90 };
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync(site);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.GetByIdAsync(testId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(90, result!.IdleLogoffMinutes);
    }

    [Fact]
    public async Task CreateAsync_DefaultsIdleLogoffMinutes_To60_WhenOmitted()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        var request = new SiteProfileRequest
        {
            SiteName = "New Clinic",
            InceptionDate = new DateTime(2025, 6, 1)
            // IdleLogoffMinutes omitted (null)
        };
        Site? captured = null;
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.AddAsync(It.IsAny<Site>()))
            .Callback<Site>(s => captured = s)
            .ReturnsAsync((Site s) => s);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.CreateAsync(request);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal(60, captured!.IdleLogoffMinutes);
        Assert.Equal(60, result.IdleLogoffMinutes);
    }

    [Fact]
    public async Task CreateAsync_UsesProvidedIdleLogoffMinutes()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        var request = new SiteProfileRequest
        {
            SiteName = "New Clinic",
            InceptionDate = new DateTime(2025, 6, 1),
            IdleLogoffMinutes = 120
        };
        Site? captured = null;
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.AddAsync(It.IsAny<Site>()))
            .Callback<Site>(s => captured = s)
            .ReturnsAsync((Site s) => s);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.CreateAsync(request);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal(120, captured!.IdleLogoffMinutes);
        Assert.Equal(120, result.IdleLogoffMinutes);
    }

    [Fact]
    public async Task UpdateAsync_SetsIdleLogoffMinutes_WhenProvided()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        int testId = 1;
        var existingSite = new Site { Id = testId, SiteName = "Clinic", IdleLogoffMinutes = 60 };
        var updateRequest = new SiteProfileUpdateRequest { IdleLogoffMinutes = 0 };  // 0 = disabled
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync(existingSite);
        _mockRepository.Setup(repo => repo.UpdateAsync(It.IsAny<Site>())).Returns(Task.CompletedTask);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.UpdateAsync(testId, updateRequest);

        // Assert
        Assert.True(result);
        Assert.Equal(0, existingSite.IdleLogoffMinutes);
    }

    [Fact]
    public async Task UpdateAsync_LeavesIdleLogoffMinutes_WhenNull()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        int testId = 1;
        var existingSite = new Site { Id = testId, SiteName = "Clinic", IdleLogoffMinutes = 45 };
        var updateRequest = new SiteProfileUpdateRequest { SiteName = "Renamed" };  // idle omitted
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync(existingSite);
        _mockRepository.Setup(repo => repo.UpdateAsync(It.IsAny<Site>())).Returns(Task.CompletedTask);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.UpdateAsync(testId, updateRequest);

        // Assert
        Assert.True(result);
        Assert.Equal(45, existingSite.IdleLogoffMinutes);
    }

    // --- WP-39 (G4): on-site trip charge field (same threading pattern as IdleLogoffMinutes) ---

    [Fact]
    public async Task GetByIdAsync_MapsOnSiteTripChargeAmount()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        int testId = 1;
        var site = new Site { Id = testId, SiteName = "Main Clinic", OnSiteTripChargeAmount = 15.50m };
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync(site);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.GetByIdAsync(testId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(15.50m, result!.OnSiteTripChargeAmount);
    }

    [Fact]
    public async Task CreateAsync_DefaultsOnSiteTripChargeAmount_To0_WhenOmitted()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        var request = new SiteProfileRequest
        {
            SiteName = "New Clinic",
            InceptionDate = new DateTime(2025, 6, 1)
            // OnSiteTripChargeAmount omitted (null)
        };
        Site? captured = null;
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.AddAsync(It.IsAny<Site>()))
            .Callback<Site>(s => captured = s)
            .ReturnsAsync((Site s) => s);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.CreateAsync(request);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal(0m, captured!.OnSiteTripChargeAmount);
        Assert.Equal(0m, result.OnSiteTripChargeAmount);
    }

    [Fact]
    public async Task CreateAsync_UsesProvidedOnSiteTripChargeAmount()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        var request = new SiteProfileRequest
        {
            SiteName = "New Clinic",
            InceptionDate = new DateTime(2025, 6, 1),
            OnSiteTripChargeAmount = 25.00m
        };
        Site? captured = null;
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.AddAsync(It.IsAny<Site>()))
            .Callback<Site>(s => captured = s)
            .ReturnsAsync((Site s) => s);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.CreateAsync(request);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal(25.00m, captured!.OnSiteTripChargeAmount);
        Assert.Equal(25.00m, result.OnSiteTripChargeAmount);
    }

    [Fact]
    public async Task UpdateAsync_SetsOnSiteTripChargeAmount_WhenProvided()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        int testId = 1;
        var existingSite = new Site { Id = testId, SiteName = "Clinic", OnSiteTripChargeAmount = 10m };
        var updateRequest = new SiteProfileUpdateRequest { OnSiteTripChargeAmount = 0m };  // 0 = no charge (legal)
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync(existingSite);
        _mockRepository.Setup(repo => repo.UpdateAsync(It.IsAny<Site>())).Returns(Task.CompletedTask);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.UpdateAsync(testId, updateRequest);

        // Assert
        Assert.True(result);
        Assert.Equal(0m, existingSite.OnSiteTripChargeAmount);
    }

    // --- WP-42 (G1): no-show fee pct (same threading pattern as the two fields above) ---

    [Fact]
    public async Task GetByIdAsync_MapsNoShowFeePct()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        int testId = 1;
        var site = new Site { Id = testId, SiteName = "Main Clinic", NoShowFeePct = 12.50m };
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync(site);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.GetByIdAsync(testId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(12.50m, result!.NoShowFeePct);
    }

    [Fact]
    public async Task CreateAsync_DefaultsNoShowFeePct_ToFullPrice_WhenOmitted()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        var request = new SiteProfileRequest
        {
            SiteName = "New Clinic",
            InceptionDate = new DateTime(2026, 7, 1)
            // NoShowFeePct omitted (null)
        };
        Site? captured = null;
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.AddAsync(It.IsAny<Site>()))
            .Callback<Site>(s => captured = s)
            .ReturnsAsync((Site s) => s);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.CreateAsync(request);

        // Assert
        Assert.NotNull(captured);
        // WP-49 (BR1/D5): the omitted-value default moved 30 → 100 (percent, not dollars).
        Assert.Equal(SiteDefaults.NoShowFeePct, captured!.NoShowFeePct);
        Assert.Equal(100.00m, captured.NoShowFeePct);
        Assert.Equal(100.00m, result.NoShowFeePct);
    }

    [Fact]
    public async Task CreateAsync_UsesProvidedNoShowFeePct()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        var request = new SiteProfileRequest
        {
            SiteName = "New Clinic",
            InceptionDate = new DateTime(2026, 7, 1),
            NoShowFeePct = 0m // 0 = no fee (legal)
        };
        Site? captured = null;
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.AddAsync(It.IsAny<Site>()))
            .Callback<Site>(s => captured = s)
            .ReturnsAsync((Site s) => s);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.CreateAsync(request);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal(0m, captured!.NoShowFeePct);
        Assert.Equal(0m, result.NoShowFeePct);
    }

    [Fact]
    public async Task UpdateAsync_SetsNoShowFeePct_WhenProvided()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        int testId = 1;
        var existingSite = new Site { Id = testId, SiteName = "Clinic", NoShowFeePct = 30m };
        var updateRequest = new SiteProfileUpdateRequest { NoShowFeePct = 12.50m };
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync(existingSite);
        _mockRepository.Setup(repo => repo.UpdateAsync(It.IsAny<Site>())).Returns(Task.CompletedTask);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.UpdateAsync(testId, updateRequest);

        // Assert
        Assert.True(result);
        Assert.Equal(12.50m, existingSite.NoShowFeePct);
    }

    [Fact]
    public async Task UpdateAsync_LeavesNoShowFeePct_WhenNull()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        int testId = 1;
        var existingSite = new Site { Id = testId, SiteName = "Clinic", NoShowFeePct = 45.25m };
        var updateRequest = new SiteProfileUpdateRequest { SiteName = "Renamed" };  // fee omitted
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync(existingSite);
        _mockRepository.Setup(repo => repo.UpdateAsync(It.IsAny<Site>())).Returns(Task.CompletedTask);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.UpdateAsync(testId, updateRequest);

        // Assert
        Assert.True(result);
        Assert.Equal(45.25m, existingSite.NoShowFeePct);
    }

    [Fact]
    public async Task UpdateAsync_LeavesOnSiteTripChargeAmount_WhenNull()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SiteProfileService>>();
        int testId = 1;
        var existingSite = new Site { Id = testId, SiteName = "Clinic", OnSiteTripChargeAmount = 12.75m };
        var updateRequest = new SiteProfileUpdateRequest { SiteName = "Renamed" };  // charge omitted
        var _mockRepository = new Mock<ISiteRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync(existingSite);
        _mockRepository.Setup(repo => repo.UpdateAsync(It.IsAny<Site>())).Returns(Task.CompletedTask);
        var svc = new SiteProfileService(fakeLogger, _mockRepository.Object);

        // Act
        var result = await svc.UpdateAsync(testId, updateRequest);

        // Assert
        Assert.True(result);
        Assert.Equal(12.75m, existingSite.OnSiteTripChargeAmount);
    }
}
