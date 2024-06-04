using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Core.BusinessObjects.Sessions;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TherapistsController : ControllerBase
{
    private readonly ITherapistProfileService _therapistProfileService;
    private readonly IHandleSessionEvent _sessionEventHandler;

    public TherapistsController(ITherapistProfileService therapistProfileService, IHandleSessionEvent sesssionEventHandler)
    {
        _therapistProfileService = therapistProfileService;
        _sessionEventHandler = sesssionEventHandler;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<TherapistProfile>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllTherapists()
    {
        var Therapists = await _therapistProfileService.GetAllAsync();
        return Ok(Therapists);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TherapistProfile))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetTherapist(int id)
    {
        var Therapist = await _therapistProfileService.GetByIdAsync(id);
        if (Therapist == null) return NotFound();
        return Ok(Therapist);
    }

    [HttpGet("{id}/pastdue")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<SessionEvent>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPastDueSessions(int id)
    {
        var pastDueSessions = await _sessionEventHandler.GetAllPastDueAsync();
        var pastDueForTherapist = pastDueSessions
            .Where(s => s.TherapistId.Equals(id))
            .Select(s => s);
        return Ok(pastDueForTherapist);
    }

    [HttpPost]
    public async Task<IActionResult> CreateTherapist([FromBody] TherapistProfileRequest therapistRequest)
    {
        var createdTherapist = await _therapistProfileService.CreateAsync(therapistRequest);
        return CreatedAtAction(nameof(GetTherapist), new { id = createdTherapist.TherapistId }, createdTherapist);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTherapist(int id, [FromBody] TherapistProfileUpdateRequest therapistRequest)
    {
        if (await _therapistProfileService.VerifyRequestAsync(id))
        {
            var updateResult = await _therapistProfileService.UpdateAsync(id, therapistRequest);
            if (updateResult)
            {
                return NoContent();
            }
            else
            {
                return BadRequest("Bad input?");
            }
        }
        return BadRequest();
    }
}
