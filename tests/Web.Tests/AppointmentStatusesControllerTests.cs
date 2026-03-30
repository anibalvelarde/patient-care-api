using Moq;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Controllers;
using Microsoft.Extensions.Logging;

namespace Web.Tests.Controllers;

public class AppointmentStatusesControllerTests
{
    private readonly Mock<IAppointmentStatusService> _mockService;
    private readonly AppointmentStatusesController _controller;

    public AppointmentStatusesControllerTests()
    {
        var fakeLogger = Mock.Of<ILogger<AppointmentStatusesController>>();
        _mockService = new Mock<IAppointmentStatusService>();
        _controller = new AppointmentStatusesController(fakeLogger, _mockService.Object);
    }

    [Fact]
    public async Task GetAll_ReturnsOkResult_WithStatuses()
    {
        // Arrange
        var mockItems = new List<AppointmentStatusInfo>
        {
            new() { AppointmentStatusId = 1, StatusName = "Proposed" },
            new() { AppointmentStatusId = 2, StatusName = "Confirmed" }
        };
        _mockService.Setup(s => s.GetAllAsync()).ReturnsAsync(mockItems);

        // Act
        var result = await _controller.GetAll();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<List<AppointmentStatusInfo>>(okResult.Value);
        Assert.Equal(2, returned.Count);
    }

    [Fact]
    public async Task GetById_ReturnsOkResult_WhenExists()
    {
        // Arrange
        var item = new AppointmentStatusInfo { AppointmentStatusId = 1, StatusName = "Proposed" };
        _mockService.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(item);

        // Act
        var result = await _controller.GetById(1);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<AppointmentStatusInfo>(okResult.Value);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenNotExists()
    {
        // Arrange
        _mockService.Setup(s => s.GetByIdAsync(99)).ReturnsAsync((AppointmentStatusInfo?)null);

        // Act
        var result = await _controller.GetById(99);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtAction()
    {
        // Arrange
        var request = new AppointmentStatusCreateRequest { StatusName = "NewStatus" };
        var created = new AppointmentStatusInfo { AppointmentStatusId = 10, StatusName = "NewStatus" };
        _mockService.Setup(s => s.CreateAsync(request)).ReturnsAsync(created);

        // Act
        var result = await _controller.Create(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var returned = Assert.IsType<AppointmentStatusInfo>(createdResult.Value);
        Assert.Equal(10, returned.AppointmentStatusId);
    }

    [Fact]
    public async Task Update_ReturnsNoContent_WhenExists()
    {
        // Arrange
        var request = new AppointmentStatusUpdateRequest { StatusName = "Updated" };
        _mockService.Setup(s => s.UpdateAsync(1, request)).ReturnsAsync(true);

        // Act
        var result = await _controller.Update(1, request);

        // Assert
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenNotExists()
    {
        // Arrange
        var request = new AppointmentStatusUpdateRequest { StatusName = "Updated" };
        _mockService.Setup(s => s.UpdateAsync(99, request)).ReturnsAsync(false);

        // Act
        var result = await _controller.Update(99, request);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }
}
