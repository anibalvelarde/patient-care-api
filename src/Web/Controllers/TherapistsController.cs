using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Interfaces;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TherapistsController : ControllerBase
{
    private readonly ITherapistProfileService _therapistProfileService;

    public TherapistsController(ITherapistProfileService therapistProfileService)
    {
        _therapistProfileService = therapistProfileService;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<TherapistProfile>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllPatients()
    {
        var patients = await _therapistProfileService.GetAllAsync();
        return Ok(patients);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TherapistProfile))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPatient(int id)
    {
        var patient = await _therapistProfileService.GetByIdAsync(id);
        if (patient == null) return NotFound();
        return Ok(patient);
    }

    [HttpPost]
    public async Task<IActionResult> CreatePatient([FromBody] TherapistProfileRequest therapistRequest)
    {
        var createdTherapist = await _therapistProfileService.CreateAsync(therapistRequest);
        return CreatedAtAction(nameof(GetPatient), new { id = createdTherapist.TherapistId }, createdTherapist);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePatient(int id, [FromBody] TherapistProfileUpdateRequest therapistRequest)
    {
        if (await _therapistProfileService.VerifyRequestAsync(id, therapistRequest))
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
