using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Sites;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Web.Authorization;

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
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AdminSitesView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<SiteProfile>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllSites()
    {
        var sites = await _siteProfileService.GetAllAsync();
        return Ok(sites);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AdminSitesView)]
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
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AdminSitesManage)]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(SiteProfile))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateSite([FromBody] SiteProfileRequest createRequest)
    {
        // WP-42 (G1): the no-show fee pct is SYSADMIN-gated by ROLE (owner ruling 2026-07-31 —
        // no claim, no matrix change). On create, a value differing from the platform default
        // needs the role; omitted/default passes any Admin.Sites.Manage holder. The pivot
        // tracks SiteDefaults.NoShowFeePct, so WP-49/BR1 moving it to 100 automatically makes
        // 100 the unprivileged value and 30 a privileged one.
        if (createRequest.NoShowFeePct.HasValue
            && createRequest.NoShowFeePct.Value != SiteDefaults.NoShowFeePct
            && !User.IsSystemAdmin())
        {
            return Forbid();
        }

        var createdSite = await _siteProfileService.CreateAsync(createRequest);
        return CreatedAtAction(nameof(GetSite), new { id = createdSite.SiteId }, createdSite);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AdminSitesManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateSite(int id, [FromBody] SiteProfileUpdateRequest updateRequest)
    {
        // WP-42 (G1): field-level ROLE gate, same present-AND-different pattern as the WP-23
        // SENADIS / WP-40 BK-3 field gates — a noShowFeePct that CHANGES the stored value
        // requires the SYSADMIN role; omitted/echoed-unchanged passes so non-SA admins keep
        // editing the other Site fields. Nothing is applied on the Forbid path.
        if (updateRequest.NoShowFeePct.HasValue)
        {
            var siteOnFile = await _siteProfileService.GetByIdAsync(id);
            if (siteOnFile is null)
            {
                return NotFound();
            }
            if (updateRequest.NoShowFeePct.Value != siteOnFile.NoShowFeePct && !User.IsSystemAdmin())
            {
                return Forbid();
            }
        }

        var updateResult = await _siteProfileService.UpdateAsync(id, updateRequest);
        if (updateResult)
        {
            return NoContent();
        }
        return NotFound();
    }
}
