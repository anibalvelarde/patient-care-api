using Moq;
using Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Controllers;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using Castle.Core.Logging;

namespace Web.Tests.Controllers;

public class CaretakersControllerTests
{
    private readonly Mock<ICaretakerProfileService> _mockService;
    private readonly CaretakersController _controller;

    public CaretakersControllerTests()
    {
        var fakeLogger = Mock.Of<ILogger<CaretakersController>>();
        _mockService = new Mock<ICaretakerProfileService>();
        var mockPaymentService = Mock.Of<IPaymentRecordService>();
        var mockStatementService = Mock.Of<IAccountStatementService>();
        _controller = new CaretakersController(fakeLogger, _mockService.Object, mockPaymentService, mockStatementService);
    }

    // WP-30 (U2): GET /api/caretakers is paged-by-default (BREAKING — was a bare array).
    [Fact]
    public async Task GetCaretakers_ReturnsOkResult_WithPagedEnvelope()
    {
        // Arrange
        var paged = new PagedResult<CaretakerProfile>
        {
            Items = new List<CaretakerProfile> { new(), new() },
            Page = 1,
            PageSize = 30,
            TotalCount = 1089,
        };
        _mockService
            .Setup(service => service.GetPagedAsync(null, null, 1, 30))
            .ReturnsAsync(paged);

        // Act
        var result = await _controller.GetCaretakers();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var envelope = Assert.IsType<PagedResult<CaretakerProfile>>(okResult.Value);
        Assert.Equal(1089, envelope.TotalCount);
        Assert.Equal(2, envelope.Items.Count);
    }

    // WP-30: shared PagingParams clamp — same lenient behavior as the patients side.
    [Theory]
    [InlineData(0, 0, 1, 30)]
    [InlineData(-5, 100000, 1, 100)]
    [InlineData(2, 40, 2, 40)]
    public async Task GetCaretakers_ClampsPagingParams(int page, int pageSize, int expectedPage, int expectedSize)
    {
        _mockService
            .Setup(s => s.GetPagedAsync(null, null, expectedPage, expectedSize))
            .ReturnsAsync(new PagedResult<CaretakerProfile>())
            .Verifiable();

        var result = await _controller.GetCaretakers(page: page, pageSize: pageSize);

        Assert.IsType<OkObjectResult>(result);
        _mockService.Verify();
    }

    // WP-30: typeahead — q required (min length 1), cap 20 server-side.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LookupCaretakers_MissingOrBlankQuery_Returns400(string? q)
    {
        var result = await _controller.LookupCaretakers(q);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        _mockService.Verify(s => s.LookupAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task LookupCaretakers_ValidQuery_ReturnsItems_WithCap20()
    {
        var items = new List<CaretakerLookupItem>
        {
            new() { CaretakerId = 1, CaretakerName = "Gomez, Ana" },
        };
        _mockService.Setup(s => s.LookupAsync("gom", 20)).ReturnsAsync(items).Verifiable();

        var result = await _controller.LookupCaretakers("gom");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsAssignableFrom<IReadOnlyList<CaretakerLookupItem>>(okResult.Value);
        Assert.Single(returned);
        _mockService.Verify();
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
