using Moq;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Controllers;
using System.Collections.Generic;
using System.Threading.Tasks;

public class CaretakersControllerTests
{
    private readonly Mock<ICaretakerProfileService> _mockService;
    private readonly CaretakersController _controller;

    public CaretakersControllerTests()
    {
        _mockService = new Mock<ICaretakerProfileService>();
        _controller = new CaretakersController(_mockService.Object);
    }

    [Fact]
    public async Task GetAllCaretakers_ReturnsOkResult_WithCaretakers()
    {
        // Arrange
        var mockCaretakers = new List<CaretakerProfile>
        {
            new CaretakerProfile { /* properties */ },
            new CaretakerProfile { /* properties */ }
        };
        _mockService.Setup(service => service.GetAllAsync()).ReturnsAsync(mockCaretakers);

        // Act
        var result = await _controller.GetAllCaretakers();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedCaretakers = Assert.IsType<List<CaretakerProfile>>(okResult.Value);
        Assert.Equal(mockCaretakers.Count, returnedCaretakers.Count);
    }

    [Fact]
    public async Task GetCaretaker_ReturnsNotFound_WhenCaretakerDoesNotExist()
    {
        // Arrange
        CaretakerProfile? nullCaretaker = null;
        _mockService.Setup(service =>
            service.GetByIdAsync(It.IsAny<int>()))
        .ReturnsAsync(value: nullCaretaker);

        // Act
        var result = await _controller.GetCaretaker(1);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetCaretaker_ReturnsOkResult_WithCaretaker()
    {
        // Arrange
        var mockCaretaker = new CaretakerProfile { /* properties */ };
        _mockService.Setup(service => service.GetByIdAsync(1)).ReturnsAsync(mockCaretaker);

        // Act
        var result = await _controller.GetCaretaker(1);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedCaretaker = Assert.IsType<CaretakerProfile>(okResult.Value);
        Assert.NotNull(returnedCaretaker);
    }
}
