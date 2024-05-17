using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects;
using Neurocorp.Api.Core.Interfaces;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PatientsController : ControllerBase
{
    private readonly IPatientProfileService _patientProfileService;

    public PatientsController(IPatientProfileService patientProfileService)
    {
        _patientProfileService = patientProfileService;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PatientProfile>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllPatients()
    {
        var patients = await _patientProfileService.GetAllAsync();
        return Ok(patients);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PatientProfile))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPatient(int id)
    {
        var patient = await _patientProfileService.GetByIdAsync(id);
        if (patient == null) return NotFound();
        return Ok(patient);
    }

    [HttpPost]
    public async Task<IActionResult> CreatePatient([FromBody] PatientProfileRequest patientRequest)
    {
        var createdPatient = await _patientProfileService.CreateAsync(patientRequest);
        return CreatedAtAction(nameof(GetPatient), new { id = createdPatient.PatientId }, createdPatient);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePatient(int id, [FromBody] PatientProfileUpdateRequest patientRequest)
    {
        if (await _patientProfileService.VerifyRequestAsync(id, patientRequest))
        {
            var updateResult = await _patientProfileService.UpdateAsync(id, patientRequest);
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
