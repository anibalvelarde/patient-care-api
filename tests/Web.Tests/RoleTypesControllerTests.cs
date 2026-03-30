using Moq;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Controllers;
using Microsoft.Extensions.Logging;

namespace Web.Tests.Controllers;

public class RoleTypesControllerTests
{
    private readonly Mock<IRoleTypeService> _mockService;
    private readonly RoleTypesController _controller;

    public RoleTypesControllerTests()
    {
        var fakeLogger = Mock.Of<ILogger<RoleTypesController>>();
        _mockService = new Mock<IRoleTypeService>();
        _controller = new RoleTypesController(fakeLogger, _mockService.Object);
    }

    [Fact]
    public async Task GetAll_ReturnsOkResult_WithRoles()
    {
        // Arrange
        var mockItems = new List<RoleTypeInfo>
        {
            new() { RoleId = 1, RoleName = "Patient" },
            new() { RoleId = 2, RoleName = "Therapist" }
        };
        _mockService.Setup(s => s.GetAllAsync()).ReturnsAsync(mockItems);

        // Act
        var result = await _controller.GetAll();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<List<RoleTypeInfo>>(okResult.Value);
        Assert.Equal(2, returned.Count);
    }

    [Fact]
    public async Task GetById_ReturnsOkResult_WhenExists()
    {
        // Arrange
        var item = new RoleTypeInfo { RoleId = 1, RoleName = "Patient" };
        _mockService.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(item);

        // Act
        var result = await _controller.GetById(1);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<RoleTypeInfo>(okResult.Value);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenNotExists()
    {
        // Arrange
        _mockService.Setup(s => s.GetByIdAsync(99)).ReturnsAsync((RoleTypeInfo?)null);

        // Act
        var result = await _controller.GetById(99);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtAction()
    {
        // Arrange
        var request = new RoleTypeCreateRequest { RoleName = "Admin" };
        var created = new RoleTypeInfo { RoleId = 10, RoleName = "Admin" };
        _mockService.Setup(s => s.CreateAsync(request)).ReturnsAsync(created);

        // Act
        var result = await _controller.Create(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var returned = Assert.IsType<RoleTypeInfo>(createdResult.Value);
        Assert.Equal(10, returned.RoleId);
    }

    [Fact]
    public async Task Update_ReturnsNoContent_WhenExists()
    {
        // Arrange
        var request = new RoleTypeUpdateRequest { RoleName = "Updated" };
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
        var request = new RoleTypeUpdateRequest { RoleName = "Updated" };
        _mockService.Setup(s => s.UpdateAsync(99, request)).ReturnsAsync(false);

        // Act
        var result = await _controller.Update(99, request);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }
}
