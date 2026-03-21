using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.BusinessObjects.Common;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PatientsController : ControllerBase
{
    private readonly IPatientProfileService _patientProfileService;
    private readonly IHandleSessionEvent _sessionEventHandler;
    private readonly ILogger<PatientsController> _logger;

    public PatientsController(ILogger<PatientsController> logger, IPatientProfileService patientProfileService, IHandleSessionEvent sessionEventHandler)
    {
        _patientProfileService = patientProfileService;
        _sessionEventHandler = sessionEventHandler;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PatientProfile>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllPatients()
    {
        var patients = await _patientProfileService.GetAllAsync();
        return Ok(patients);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PatientProfile))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPatient(int id)
    {
        var patient = await _patientProfileService.GetByIdAsync(id);
        if (patient == null) return NotFound();
        return Ok(patient);
    }

    [HttpGet("{id:int}/pastdue")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PatientPastDueInfo))]
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

    [HttpGet("pastdue")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PatientPastDueInfo>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllPastDuePatients()
    {
        var pastDuePatients = await _sessionEventHandler.GetAllPatientsPastDueAsync();
        return Ok(pastDuePatients);
    }

    [HttpGet("{id:int}/caretakers")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PatientCaretakerSummary>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPatientCaretakers(int id)
    {
        var caretakers = await _patientProfileService.GetCaretakersForPatientAsync(id);
        return Ok(caretakers);
    }

    [HttpPost]
    public async Task<IActionResult> CreatePatient([FromBody] PatientProfileRequest patientRequest)
    {
        var createdPatient = await _patientProfileService.CreateAsync(patientRequest);
        return CreatedAtAction(nameof(CreatePatient), new { id = createdPatient.PatientId }, createdPatient);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdatePatient(int id, [FromBody] PatientProfileUpdateRequest patientRequest)
    {
        if (await _patientProfileService.VerifyRequestAsync(id))
        {
            try
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
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }
        return BadRequest();
    }

    private async Task<PatientPastDueInfo> PackagePastDueInfoAsync(int patientId)
    {
        var patient = await _patientProfileService.GetByIdAsync(patientId);
        if (patient is not null)
        {
            var pastDueSessions = await _sessionEventHandler.GetAllPastDueAsync();
            var patientPastDueSessions = pastDueSessions
                .Where(s => s.PatientId.Equals(patientId))
                .Select(s => s);
            var totalPastDueAmount = patientPastDueSessions.Sum(s => s.AmountDue);
            var totalPaidSoFar = patientPastDueSessions.Sum(s => s.AmountPaid);
            _logger.LogInformation(
                "Patient [{patientName}] has {Count} sessions that are past-due. PastDue:{d} PaidSoFar:{d} ",
                patient!.PatientName, patientPastDueSessions.Count(), totalPastDueAmount, totalPaidSoFar);

            return new PatientPastDueInfo
            {
                Party = patient,
                PastDueSessions = patientPastDueSessions.Count(),
                PastDueTotalAmount = totalPastDueAmount,
                AmountPaidSoFar = totalPaidSoFar,
                Delinquency = patientPastDueSessions,
            };            
        }
        return new PatientPastDueInfo() { Party = new NotFoundProfile() };
    }
}
