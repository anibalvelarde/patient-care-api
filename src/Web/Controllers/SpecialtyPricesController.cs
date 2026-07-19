using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Authorization;

namespace Neurocorp.Api.Web.Controllers;

/// <summary>
/// WP-39: per-duration specialty price sheet (temporal, append-only).
///
/// Routed under the specialty-types lookup resource (contract: lookups-api.md), but a separate
/// controller — the routes are specialty-only, so unlike the generic <c>LookupsController</c>
/// they can carry static [Authorize] policies:
///   * GET rides the existing <c>Admin.Lookups.SpecialtyType.View</c> (MGR, AM as of
///     57b6150a350c, OWN + SYSADMIN) — the admin modal's read;
///   * PUT is gated by the NEW narrow claim <c>Specialties.Prices.Edit</c> (MGR, AM + SYSADMIN
///     wildcard; G3b) — price editing WITHOUT structural lookup-manage rights.
/// </summary>
[ApiController]
[Route("api/lookups/specialty-types")]
public class SpecialtyPricesController : ControllerBase
{
    private readonly ISpecialtyPriceService _priceService;

    public SpecialtyPricesController(ISpecialtyPriceService priceService)
    {
        _priceService = priceService;
    }

    [HttpGet("{id}/prices")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AdminLookupsSpecialtyTypeView)]
    [ProducesResponseType(typeof(SpecialtyPriceHistory), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPrices(int id)
    {
        var history = await _priceService.GetHistoryAsync(id);
        if (history == null) return NotFound();
        return Ok(history);
    }

    [HttpPut("{id}/prices")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.SpecialtiesPricesEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PutPrices(int id, [FromBody] SpecialtyPricesPutRequest request)
    {
        // Durations {30,45,60,90,120} / amount ≥ 0 / effectiveFrom required are DataAnnotations
        // on the request DTO — [ApiController] turns violations into an automatic 400.
        var result = await _priceService.AppendAsync(id, request);
        return result switch
        {
            PriceAppendResult.SpecialtyNotFound => NotFound(),
            PriceAppendResult.DuplicateRow => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: "A price row with the same durationMinutes and effectiveFrom already " +
                        "exists (or is repeated in this request). History is append-only — " +
                        "submit the correction with a different effective date."),
            _ => NoContent(),
        };
    }
}
