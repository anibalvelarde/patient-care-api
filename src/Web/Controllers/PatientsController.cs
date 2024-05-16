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
    public async Task<IActionResult> GetAllPatients()
    {
        var patients = await _patientProfileService.GetAllAsync();
        return Ok(patients);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetPatient(int id)
    {
        var patient = await _patientProfileService.GetByIdAsync(id);
        if (patient == null) return NotFound();
        return Ok(patient);
    }

    [HttpPost]
    public async Task<IActionResult> CreatePatient([FromBody] PatientProfile patient)
    {
        var createdPatient = await _patientProfileService.CreateAsync(patient);
        return CreatedAtAction(nameof(GetPatient), new { id = createdPatient.PatientId }, createdPatient);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePatient(int id, [FromBody] PatientProfile patient)
    {
        if (id != patient.PatientId)
            return BadRequest();

        await _patientProfileService.UpdateAsync(patient);
        return NoContent();
    }
}
