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

    public CaretakersController(ICaretakerProfileService caretakerProfileService)
    {
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
}
