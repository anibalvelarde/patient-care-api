using Moq;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Controllers;
using Microsoft.Extensions.Logging;

namespace Web.Tests.Controllers;

public class SpecialtyTypesControllerTests
{
    private readonly Mock<ISpecialtyTypeService> _mockService;
    private readonly SpecialtyTypesController _controller;

    public SpecialtyTypesControllerTests()
    {
        var fakeLogger = Mock.Of<ILogger<SpecialtyTypesController>>();
        _mockService = new Mock<ISpecialtyTypeService>();
        _controller = new SpecialtyTypesController(fakeLogger, _mockService.Object);
    }

    [Fact]
    public async Task GetAll_ReturnsOkResult_WithSpecialties()
    {
        // Arrange
        var mockItems = new List<SpecialtyTypeInfo>
        {
            new() { SpecialtyId = 1, Abbreviation = "ET", Name = "Early Stimulation" },
            new() { SpecialtyId = 2, Abbreviation = "FS", Name = "Physiotherapy" }
        };
        _mockService.Setup(s => s.GetAllAsync()).ReturnsAsync(mockItems);

        // Act
        var result = await _controller.GetAll();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<List<SpecialtyTypeInfo>>(okResult.Value);
        Assert.Equal(2, returned.Count);
    }

    [Fact]
    public async Task GetById_ReturnsOkResult_WhenExists()
    {
        // Arrange
        var item = new SpecialtyTypeInfo { SpecialtyId = 1, Abbreviation = "ET", Name = "Early Stimulation" };
        _mockService.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(item);

        // Act
        var result = await _controller.GetById(1);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<SpecialtyTypeInfo>(okResult.Value);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenNotExists()
    {
        // Arrange
        _mockService.Setup(s => s.GetByIdAsync(99)).ReturnsAsync((SpecialtyTypeInfo?)null);

        // Act
        var result = await _controller.GetById(99);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtAction()
    {
        // Arrange
        var request = new SpecialtyTypeCreateRequest { Abbreviation = "OT", Name = "Occupational Therapy" };
        var created = new SpecialtyTypeInfo { SpecialtyId = 10, Abbreviation = "OT", Name = "Occupational Therapy" };
        _mockService.Setup(s => s.CreateAsync(request)).ReturnsAsync(created);

        // Act
        var result = await _controller.Create(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var returned = Assert.IsType<SpecialtyTypeInfo>(createdResult.Value);
        Assert.Equal(10, returned.SpecialtyId);
    }

    [Fact]
    public async Task Update_ReturnsNoContent_WhenExists()
    {
        // Arrange
        var request = new SpecialtyTypeUpdateRequest { Name = "Updated" };
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
        var request = new SpecialtyTypeUpdateRequest { Name = "Updated" };
        _mockService.Setup(s => s.UpdateAsync(99, request)).ReturnsAsync(false);

        // Act
        var result = await _controller.Update(99, request);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }
}
