using Microsoft.AspNetCore.Mvc;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Controllers;

namespace Web.Tests.Controllers;

public class LookupsControllerTests
{
    private readonly Mock<ILookupService> _mockService;
    private readonly LookupsController _controller;

    public LookupsControllerTests()
    {
        _mockService = new Mock<ILookupService>();
        _controller = new LookupsController(_mockService.Object);
    }

    // --- GetAll ---

    [Fact]
    public async Task GetAll_ReturnsOk_WithItems_ForValidTableName()
    {
        // Arrange
        var items = new List<LookupItem>
        {
            new() { Id = 1, Abbreviation = "PROP", Name = "Proposed" },
            new() { Id = 2, Abbreviation = "CONF", Name = "Confirmed" }
        };
        _mockService.Setup(s => s.IsValidTableName("appointment-statuses")).Returns(true);
        _mockService.Setup(s => s.GetAllAsync("appointment-statuses")).ReturnsAsync(items);

        // Act
        var result = await _controller.GetAll("appointment-statuses");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsAssignableFrom<IEnumerable<LookupItem>>(okResult.Value);
        Assert.Equal(2, returned.Count());
    }

    [Fact]
    public async Task GetAll_ReturnsBadRequest_ForInvalidTableName()
    {
        // Arrange
        _mockService.Setup(s => s.IsValidTableName("invalid")).Returns(false);

        // Act
        var result = await _controller.GetAll("invalid");

        // Assert
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
        Assert.IsType<ProblemDetails>(problem.Value);
    }

    // --- GetById ---

    [Fact]
    public async Task GetById_ReturnsOk_WhenItemExists()
    {
        // Arrange
        var item = new LookupItem { Id = 1, Abbreviation = "PROP", Name = "Proposed" };
        _mockService.Setup(s => s.IsValidTableName("appointment-statuses")).Returns(true);
        _mockService.Setup(s => s.GetByIdAsync("appointment-statuses", 1)).ReturnsAsync(item);

        // Act
        var result = await _controller.GetById("appointment-statuses", 1);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<LookupItem>(okResult.Value);
        Assert.Equal(1, returned.Id);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenItemNotExists()
    {
        // Arrange
        _mockService.Setup(s => s.IsValidTableName("appointment-statuses")).Returns(true);
        _mockService.Setup(s => s.GetByIdAsync("appointment-statuses", 99)).ReturnsAsync((LookupItem?)null);

        // Act
        var result = await _controller.GetById("appointment-statuses", 99);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetById_ReturnsBadRequest_ForInvalidTableName()
    {
        // Arrange
        _mockService.Setup(s => s.IsValidTableName("invalid")).Returns(false);

        // Act
        var result = await _controller.GetById("invalid", 1);

        // Assert
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
        Assert.IsType<ProblemDetails>(problem.Value);
    }

    // --- Create ---

    [Fact]
    public async Task Create_ReturnsCreatedAtAction_ForValidTableName()
    {
        // Arrange
        var request = new LookupCreateRequest { Abbreviation = "NEW", Name = "NewItem" };
        var created = new LookupItem { Id = 10, Abbreviation = "NEW", Name = "NewItem" };
        _mockService.Setup(s => s.IsValidTableName("role-types")).Returns(true);
        _mockService.Setup(s => s.CreateAsync("role-types", request)).ReturnsAsync(created);

        // Act
        var result = await _controller.Create("role-types", request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var returned = Assert.IsType<LookupItem>(createdResult.Value);
        Assert.Equal(10, returned.Id);
        Assert.Equal("NEW", returned.Abbreviation);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_ForInvalidTableName()
    {
        // Arrange
        var request = new LookupCreateRequest { Abbreviation = "NEW", Name = "NewItem" };
        _mockService.Setup(s => s.IsValidTableName("invalid")).Returns(false);

        // Act
        var result = await _controller.Create("invalid", request);

        // Assert
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
        Assert.IsType<ProblemDetails>(problem.Value);
    }

    // --- Update ---

    [Fact]
    public async Task Update_ReturnsNoContent_WhenUpdateSucceeds()
    {
        // Arrange
        var request = new LookupUpdateRequest { Name = "Updated" };
        _mockService.Setup(s => s.IsValidTableName("payment-types")).Returns(true);
        _mockService.Setup(s => s.UpdateAsync("payment-types", 1, request)).ReturnsAsync(true);

        // Act
        var result = await _controller.Update("payment-types", 1, request);

        // Assert
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenEntityNotExists()
    {
        // Arrange
        var request = new LookupUpdateRequest { Name = "Updated" };
        _mockService.Setup(s => s.IsValidTableName("payment-types")).Returns(true);
        _mockService.Setup(s => s.UpdateAsync("payment-types", 99, request)).ReturnsAsync(false);

        // Act
        var result = await _controller.Update("payment-types", 99, request);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Update_ReturnsBadRequest_ForInvalidTableName()
    {
        // Arrange
        var request = new LookupUpdateRequest { Name = "Updated" };
        _mockService.Setup(s => s.IsValidTableName("invalid")).Returns(false);

        // Act
        var result = await _controller.Update("invalid", 1, request);

        // Assert
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
        Assert.IsType<ProblemDetails>(problem.Value);
    }
}
