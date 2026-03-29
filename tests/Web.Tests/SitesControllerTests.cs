using Moq;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Sites;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Controllers;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Web.Tests.Controllers;

public class SitesControllerTests
{
    private readonly Mock<ISiteProfileService> _mockService;
    private readonly SitesController _controller;

    public SitesControllerTests()
    {
        var fakeLogger = Mock.Of<ILogger<SitesController>>();
        _mockService = new Mock<ISiteProfileService>();
        _controller = new SitesController(fakeLogger, _mockService.Object);
    }

    [Fact]
    public async Task GetAllSites_ReturnsOkResult_WithSites()
    {
        // Arrange
        var mockSites = new List<SiteProfile>
        {
            new() { SiteId = 1, SiteName = "Clinic A" },
            new() { SiteId = 2, SiteName = "Clinic B" }
        };
        _mockService.Setup(service => service.GetAllAsync()).ReturnsAsync(mockSites);

        // Act
        var result = await _controller.GetAllSites();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedSites = Assert.IsType<List<SiteProfile>>(okResult.Value);
        Assert.Equal(mockSites.Count, returnedSites.Count);
    }

    [Fact]
    public async Task GetSite_ReturnsNotFound_WhenSiteDoesNotExist()
    {
        // Arrange
        SiteProfile? nullSite = null;
        _mockService.Setup(service =>
            service.GetByIdAsync(It.IsAny<int>()))
        .ReturnsAsync(value: nullSite);

        // Act
        var result = await _controller.GetSite(1);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetSite_ReturnsOkResult_WithSite()
    {
        // Arrange
        var mockSite = new SiteProfile { SiteId = 1, SiteName = "Main Clinic" };
        _mockService.Setup(service => service.GetByIdAsync(1)).ReturnsAsync(mockSite);

        // Act
        var result = await _controller.GetSite(1);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedSite = Assert.IsType<SiteProfile>(okResult.Value);
        Assert.NotNull(returnedSite);
    }

    [Fact]
    public async Task CreateSite_ReturnsCreatedAtActionResult()
    {
        // Arrange
        var request = new SiteProfileRequest
        {
            SiteName = "New Clinic",
            InceptionDate = new DateTime(2025, 1, 1)
        };
        var createdSite = new SiteProfile { SiteId = 5, SiteName = "New Clinic" };
        _mockService.Setup(service => service.CreateAsync(request)).ReturnsAsync(createdSite);

        // Act
        var result = await _controller.CreateSite(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var returnedSite = Assert.IsType<SiteProfile>(createdResult.Value);
        Assert.Equal(5, returnedSite.SiteId);
    }

    [Fact]
    public async Task UpdateSite_ReturnsNoContent_WhenSiteExists()
    {
        // Arrange
        var updateRequest = new SiteProfileUpdateRequest { SiteName = "Updated Name" };
        _mockService.Setup(service => service.UpdateAsync(1, updateRequest)).ReturnsAsync(true);

        // Act
        var result = await _controller.UpdateSite(1, updateRequest);

        // Assert
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdateSite_ReturnsNotFound_WhenSiteDoesNotExist()
    {
        // Arrange
        var updateRequest = new SiteProfileUpdateRequest { SiteName = "Updated Name" };
        _mockService.Setup(service => service.UpdateAsync(99, updateRequest)).ReturnsAsync(false);

        // Act
        var result = await _controller.UpdateSite(99, updateRequest);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }
}
