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
}
