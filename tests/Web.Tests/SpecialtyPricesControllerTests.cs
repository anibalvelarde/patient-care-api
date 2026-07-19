using Microsoft.AspNetCore.Mvc;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Controllers;

namespace Neurocorp.Api.Web.Tests;

/// <summary>
/// WP-39: status-code mapping for the specialty price endpoints —
/// GET history (200 / 404) and append-only PUT (204 / 404 / 409; the 400s are DataAnnotations
/// on the DTO, pinned in Core.Tests.SpecialtyPriceValidationTests). Authorization outcomes are
/// covered end-to-end in Authorization/SensitiveCellAuthorizationTests.
/// </summary>
public class SpecialtyPricesControllerTests
{
    private readonly Mock<ISpecialtyPriceService> _service = new();
    private readonly SpecialtyPricesController _controller;

    public SpecialtyPricesControllerTests()
    {
        _controller = new SpecialtyPricesController(_service.Object);
    }

    private static SpecialtyPricesPutRequest AnyRequest() => new()
    {
        Prices =
        [
            new SpecialtyPriceAppendRow
            {
                DurationMinutes = 60,
                Amount = 45.00m,
                EffectiveFrom = new DateOnly(2026, 8, 1),
            },
        ],
    };

    [Fact]
    public async Task GetPrices_ReturnsOk_WithHistoryPayload()
    {
        var history = new SpecialtyPriceHistory { SpecialtyTypeId = 3, DefaultAmount = 40.00m, OfferedOnSite = true };
        _service.Setup(s => s.GetHistoryAsync(3)).ReturnsAsync(history);

        var result = await _controller.GetPrices(3);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(history, ok.Value);
    }

    [Fact]
    public async Task GetPrices_UnknownSpecialty_Returns404()
    {
        _service.Setup(s => s.GetHistoryAsync(99)).ReturnsAsync((SpecialtyPriceHistory?)null);

        var result = await _controller.GetPrices(99);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PutPrices_Success_Returns204()
    {
        _service.Setup(s => s.AppendAsync(3, It.IsAny<SpecialtyPricesPutRequest>()))
            .ReturnsAsync(PriceAppendResult.Success);

        var result = await _controller.PutPrices(3, AnyRequest());

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task PutPrices_UnknownSpecialty_Returns404()
    {
        _service.Setup(s => s.AppendAsync(99, It.IsAny<SpecialtyPricesPutRequest>()))
            .ReturnsAsync(PriceAppendResult.SpecialtyNotFound);

        var result = await _controller.PutPrices(99, AnyRequest());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PutPrices_DuplicateRow_Returns409Problem()
    {
        _service.Setup(s => s.AppendAsync(3, It.IsAny<SpecialtyPricesPutRequest>()))
            .ReturnsAsync(PriceAppendResult.DuplicateRow);

        var result = await _controller.PutPrices(3, AnyRequest());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, problem.StatusCode);
        Assert.IsType<ProblemDetails>(problem.Value);
    }
}
