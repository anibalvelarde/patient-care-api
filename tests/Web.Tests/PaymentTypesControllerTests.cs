using Moq;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Payments;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Controllers;
using Microsoft.Extensions.Logging;

namespace Web.Tests.Controllers;

public class PaymentTypesControllerTests
{
    private readonly Mock<IPaymentTypeService> _mockService;
    private readonly PaymentTypesController _controller;

    public PaymentTypesControllerTests()
    {
        var fakeLogger = Mock.Of<ILogger<PaymentTypesController>>();
        _mockService = new Mock<IPaymentTypeService>();
        _controller = new PaymentTypesController(fakeLogger, _mockService.Object);
    }

    [Fact]
    public async Task GetAll_ReturnsOkResult_WithTypes()
    {
        // Arrange
        var mockItems = new List<PaymentTypeInfo>
        {
            new() { PaymentTypeId = 1, Abbreviation = "CSH", Name = "Cash" },
            new() { PaymentTypeId = 2, Abbreviation = "CHK", Name = "Check" }
        };
        _mockService.Setup(s => s.GetAllAsync()).ReturnsAsync(mockItems);

        // Act
        var result = await _controller.GetAll();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<List<PaymentTypeInfo>>(okResult.Value);
        Assert.Equal(2, returned.Count);
    }

    [Fact]
    public async Task GetById_ReturnsOkResult_WhenExists()
    {
        // Arrange
        var item = new PaymentTypeInfo { PaymentTypeId = 1, Abbreviation = "CSH", Name = "Cash" };
        _mockService.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(item);

        // Act
        var result = await _controller.GetById(1);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<PaymentTypeInfo>(okResult.Value);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenNotExists()
    {
        // Arrange
        _mockService.Setup(s => s.GetByIdAsync(99)).ReturnsAsync((PaymentTypeInfo?)null);

        // Act
        var result = await _controller.GetById(99);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtAction()
    {
        // Arrange
        var request = new PaymentTypeCreateRequest { Abbreviation = "ZEL", Name = "Zelle" };
        var created = new PaymentTypeInfo { PaymentTypeId = 10, Abbreviation = "ZEL", Name = "Zelle" };
        _mockService.Setup(s => s.CreateAsync(request)).ReturnsAsync(created);

        // Act
        var result = await _controller.Create(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var returned = Assert.IsType<PaymentTypeInfo>(createdResult.Value);
        Assert.Equal(10, returned.PaymentTypeId);
    }

    [Fact]
    public async Task Update_ReturnsNoContent_WhenExists()
    {
        // Arrange
        var request = new PaymentTypeUpdateRequest { Name = "Updated" };
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
        var request = new PaymentTypeUpdateRequest { Name = "Updated" };
        _mockService.Setup(s => s.UpdateAsync(99, request)).ReturnsAsync(false);

        // Act
        var result = await _controller.Update(99, request);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }
}
