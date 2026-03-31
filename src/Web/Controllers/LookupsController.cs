using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/lookups/{tableName}")]
public class LookupsController : ControllerBase
{
    private readonly ILookupService _lookupService;

    public LookupsController(ILookupService lookupService)
    {
        _lookupService = lookupService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<LookupItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(string tableName)
    {
        if (!_lookupService.IsValidTableName(tableName))
            return BadRequest($"Invalid lookup table name: {tableName}");

        var items = await _lookupService.GetAllAsync(tableName);
        return Ok(items);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(LookupItem), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string tableName, int id)
    {
        if (!_lookupService.IsValidTableName(tableName))
            return BadRequest($"Invalid lookup table name: {tableName}");

        var item = await _lookupService.GetByIdAsync(tableName, id);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpPost]
    [ProducesResponseType(typeof(LookupItem), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(string tableName, [FromBody] LookupCreateRequest request)
    {
        if (!_lookupService.IsValidTableName(tableName))
            return BadRequest($"Invalid lookup table name: {tableName}");

        var created = await _lookupService.CreateAsync(tableName, request);
        return CreatedAtAction(nameof(GetById), new { tableName, id = created.Id }, created);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string tableName, int id, [FromBody] LookupUpdateRequest request)
    {
        if (!_lookupService.IsValidTableName(tableName))
            return BadRequest($"Invalid lookup table name: {tableName}");

        var updated = await _lookupService.UpdateAsync(tableName, id, request);
        if (!updated) return NotFound();
        return NoContent();
    }
}
