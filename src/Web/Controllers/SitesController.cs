using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Sites;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SitesController : ControllerBase
{
    private readonly ISiteProfileService _siteProfileService;
    private readonly ILogger<SitesController> _logger;

    public SitesController(
        ILogger<SitesController> logger,
        ISiteProfileService siteProfileService)
    {
        _logger = logger;
        _siteProfileService = siteProfileService;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<SiteProfile>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllSites()
    {
        var sites = await _siteProfileService.GetAllAsync();
        return Ok(sites);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SiteProfile))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSite(int id)
    {
        var site = await _siteProfileService.GetByIdAsync(id);
        if (site == null) return NotFound();
        return Ok(site);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(SiteProfile))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateSite([FromBody] SiteProfileRequest createRequest)
    {
        var createdSite = await _siteProfileService.CreateAsync(createRequest);
        return CreatedAtAction(nameof(GetSite), new { id = createdSite.SiteId }, createdSite);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateSite(int id, [FromBody] SiteProfileUpdateRequest updateRequest)
    {
        var updateResult = await _siteProfileService.UpdateAsync(id, updateRequest);
        if (updateResult)
        {
            return NoContent();
        }
        return NotFound();
    }
}
