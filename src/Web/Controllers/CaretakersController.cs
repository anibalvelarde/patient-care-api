using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Interfaces;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CaretakersController : ControllerBase
{
    private readonly ICaretakerProfileService _caretakerProfileService;
    private readonly ILogger<CaretakersController> _logger;

    public CaretakersController(ILogger<CaretakersController> logger, ICaretakerProfileService caretakerProfileService)
    {
        _logger = logger;
        _caretakerProfileService = caretakerProfileService;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<CaretakerProfile>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllCaretakers()
    {
        var Caretakers = await _caretakerProfileService.GetAllAsync();
        return Ok(Caretakers);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(CaretakerProfile))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCaretaker(int id)
    {
        var Caretaker = await _caretakerProfileService.GetByIdAsync(id);
        if (Caretaker == null) return NotFound();
        return Ok(Caretaker);
    }

    [HttpPost]
    public async Task<IActionResult> CreateCaretaker([FromBody] CaretakerProfileRequest createRequest)
    {
        var createdCaretaker = await _caretakerProfileService.CreateAsync(createRequest);
        return CreatedAtAction(nameof(GetCaretaker), new { id = createdCaretaker.CaretakerId }, createdCaretaker);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCaretaker(int id, [FromBody] CaretakerProfileUpdateRequest updateRequest)
    {
        if (await _caretakerProfileService.VerifyRequestAsync(id, updateRequest))
        {
            var updateResult = await _caretakerProfileService.UpdateAsync(id, updateRequest);
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

    [HttpGet("{id}/patients")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<CaretakerPatientSummary>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCaretakerPatients(int id)
    {
        var patients = await _caretakerProfileService.GetPatientsForCaretakerAsync(id);
        return Ok(patients);
    }

    [HttpPost("{id}/patients")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> LinkPatient(int id, [FromBody] PatientLinkRequest request)
    {
        var result = await _caretakerProfileService.LinkPatientAsync(id, request.PatientId, request.IsPrimary, request.Relationship);
        if (result)
        {
            return Created($"/api/caretakers/{id}/patients", null);
        }
        return BadRequest("Link already exists or invalid data.");
    }

    [HttpDelete("{id}/patients/{patientId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UnlinkPatient(int id, int patientId)
    {
        var result = await _caretakerProfileService.UnlinkPatientAsync(id, patientId);
        if (result)
        {
            return NoContent();
        }
        return NotFound();
    }
}
