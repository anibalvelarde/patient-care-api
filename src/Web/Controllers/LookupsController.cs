using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Authorization;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/lookups/{tableName}")]
public class LookupsController : ControllerBase
{
    // WP-17 (D-10): this controller is generic over lookup type (the {tableName} route segment), so a
    // single static [Authorize(Policy=…)] attribute can't express the per-type
    // Admin.Lookups.<Type>.Manage claims. Create/Update instead carry [LookupManageAuthorize], a
    // route-aware authorization filter that picks the matching Manage claim from {tableName} and runs
    // before model binding (so a denied caller gets 403, not a body-validation 400). Those two actions
    // are listed in AccessControlConformanceTests.ImperativelyAuthorized (NOT the authenticated-only
    // fallback baseline). All four Manage claims are SYSADMIN-only today.
    private readonly ILookupService _lookupService;

    public LookupsController(ILookupService lookupService)
    {
        _lookupService = lookupService;
    }

    // WP-17 access-control (decision D-1): the lookup READ endpoints (GetAll/GetById) are
    // intentionally authenticated-only — runtime reference data every role needs to populate
    // dropdowns. They are deliberately NOT gated behind Admin.Lookups.*.View, which governs the
    // admin management screen. Tracked as undecorated in conformance-baseline.txt. (Manage —
    // Create/Update — is authorized per-type imperatively; see AuthorizeManageAsync above, D-10.)
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<LookupItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(string tableName)
    {
        if (!_lookupService.IsValidTableName(tableName))
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                detail: $"Invalid lookup table name: {tableName}", title: "Bad Request");

        var items = await _lookupService.GetAllAsync(tableName);
        return Ok(items);
    }

    // WP-17 D-1: authenticated-only reference read (see GetAll above).
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(LookupItem), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string tableName, int id)
    {
        if (!_lookupService.IsValidTableName(tableName))
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                detail: $"Invalid lookup table name: {tableName}", title: "Bad Request");

        var item = await _lookupService.GetByIdAsync(tableName, id);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpPost]
    [LookupManageAuthorize] // WP-17 (D-10): per-type Admin.Lookups.<Type>.Manage, resolved from {tableName}.
    [ProducesResponseType(typeof(LookupItem), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(string tableName, [FromBody] LookupCreateRequest request)
    {
        if (!_lookupService.IsValidTableName(tableName))
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                detail: $"Invalid lookup table name: {tableName}", title: "Bad Request");

        var created = await _lookupService.CreateAsync(tableName, request);
        return CreatedAtAction(nameof(GetById), new { tableName, id = created.Id }, created);
    }

    [HttpPut("{id}")]
    [LookupManageAuthorize] // WP-17 (D-10): per-type Admin.Lookups.<Type>.Manage, resolved from {tableName}.
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string tableName, int id, [FromBody] LookupUpdateRequest request)
    {
        if (!_lookupService.IsValidTableName(tableName))
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                detail: $"Invalid lookup table name: {tableName}", title: "Bad Request");

        var updated = await _lookupService.UpdateAsync(tableName, id, request);
        if (!updated) return NotFound();
        return NoContent();
    }
}
