using Moq;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Controllers;
using System.Collections.Generic;
using System.Threading.Tasks;

public class TherapistsControllerTests
{
    private readonly Mock<ITherapistProfileService> _mockService;
    private readonly TherapistsController _controller;

    public TherapistsControllerTests()
    {
        _mockService = new Mock<ITherapistProfileService>();
        _controller = new TherapistsController(_mockService.Object);
    }

    [Fact]
    public async Task GetAllTherapists_ReturnsOkResult_WithTherapists()
    {
        // Arrange
        var mockTherapists = new List<TherapistProfile>
        {
            new TherapistProfile { /* properties */ },
            new TherapistProfile { /* properties */ }
        };
        _mockService.Setup(service => service.GetAllAsync()).ReturnsAsync(mockTherapists);

        // Act
        var result = await _controller.GetAllTherapists();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedTherapists = Assert.IsType<List<TherapistProfile>>(okResult.Value);
        Assert.Equal(mockTherapists.Count, returnedTherapists.Count);
    }

    [Fact]
    public async Task GetTherapist_ReturnsNotFound_WhenTherapistDoesNotExist()
    {
        // Arrange
        TherapistProfile? nullTherapist = null;
        _mockService.Setup(service =>
            service.GetByIdAsync(It.IsAny<int>()))
        .ReturnsAsync(value: nullTherapist);

        // Act
        var result = await _controller.GetTherapist(1);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetTherapist_ReturnsOkResult_WithTherapist()
    {
        // Arrange
        var mockTherapist = new TherapistProfile { /* properties */ };
        _mockService.Setup(service => service.GetByIdAsync(1)).ReturnsAsync(mockTherapist);

        // Act
        var result = await _controller.GetTherapist(1);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedTherapist = Assert.IsType<TherapistProfile>(okResult.Value);
        Assert.NotNull(returnedTherapist);
    }
}
