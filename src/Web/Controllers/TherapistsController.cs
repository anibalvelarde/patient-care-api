using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.BusinessObjects.Common;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TherapistsController : ControllerBase
{
    private readonly ITherapistProfileService _therapistProfileService;
    private readonly IHandleSessionEvent _sessionEventHandler;
    private readonly ILogger<TherapistsController> _logger;

    public TherapistsController(ILogger<TherapistsController> logger, ITherapistProfileService therapistProfileService, IHandleSessionEvent sesssionEventHandler)
    {
        _therapistProfileService = therapistProfileService;
        _sessionEventHandler = sesssionEventHandler;
        _logger = logger;
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
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TherapistPastDueInfo))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPastDueSessions(int id)
    {
        var pastDueInfo = await PackagePastDueInfoAsync(id);
        if (pastDueInfo is not null && pastDueInfo.Party!.IsValid)
        {
            return Ok(pastDueInfo);  
        }
        return NotFound();
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

    private async Task<TherapistPastDueInfo> PackagePastDueInfoAsync(int patientId)
    {
        var therapist = await _therapistProfileService.GetByIdAsync(patientId);
        if (therapist is not null)
        {
            var pastDueSessions = await _sessionEventHandler.GetAllPastDueAsync();
            var patientPastDueSessions = pastDueSessions
                .Where(s => s.PatientId.Equals(patientId))
                .Select(s => s);
            var totalPastDueAmount = patientPastDueSessions.Sum(s => s.AmountDue);
            var totalPaidSoFar = patientPastDueSessions.Sum(s => s.AmountPaid);
            _logger.LogInformation(
                "Therapist [{name}] has {Count} sessions where their customers are past-due. PastDue:{d} PaidSoFar:{d} ",
                therapist!.TherapistName, patientPastDueSessions.Count(), totalPastDueAmount, totalPaidSoFar);

            return new TherapistPastDueInfo
            {
                Party = therapist,
                PastDueSessions = patientPastDueSessions.Count(),
                PastDueTotalAmount = totalPastDueAmount,
                AmountPaidSoFar = totalPaidSoFar,
                Delinquency = patientPastDueSessions,
            };    
        }        
        return new TherapistPastDueInfo() { Party = new NotFoundProfile() };
    }    
}
