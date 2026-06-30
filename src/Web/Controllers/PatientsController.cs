using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Web.Authorization;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PatientsController : ControllerBase
{
    private readonly IPatientProfileService _patientProfileService;
    private readonly IHandleSessionEvent _sessionEventHandler;
    private readonly ISessionEventRepository _sessionEventRepository;
    private readonly ILogger<PatientsController> _logger;

    public PatientsController(
        ILogger<PatientsController> logger,
        IPatientProfileService patientProfileService,
        IHandleSessionEvent sessionEventHandler,
        ISessionEventRepository sessionEventRepository)
    {
        _patientProfileService = patientProfileService;
        _sessionEventHandler = sessionEventHandler;
        _sessionEventRepository = sessionEventRepository;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PatientProfile>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllPatients()
    {
        var patients = await _patientProfileService.GetAllAsync();
        return Ok(patients);
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsView)]
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
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsDelinquentView)]
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
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsDelinquentView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PatientPastDueInfo>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllPastDuePatients()
    {
        var pastDuePatients = await _sessionEventHandler.GetAllPatientsPastDueAsync();
        return Ok(pastDuePatients);
    }

    [HttpGet("{id:int}/caretakers")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PatientCaretakerSummary>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPatientCaretakers(int id)
    {
        var caretakers = await _patientProfileService.GetCaretakersForPatientAsync(id);
        return Ok(caretakers);
    }

    // WP-17 (D-8): cross-resource read gated by the data's own domain claim (Appointments.View) —
    // these are the patient's session/appointment rows. AM/FD/MGR.
    [HttpGet("{patientId}/sessions")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AppointmentsView)]
    [ProducesResponseType(typeof(IEnumerable<SessionEvent>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPatientSessions(
        int patientId,
        [FromQuery] bool? isDiscovery = null,
        [FromQuery] string? status = null)
    {
        var sessions = await _sessionEventRepository.GetByPatientIdAsync(patientId, isDiscovery, status);
        return Ok(sessions);
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsEdit)]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(PatientProfile))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePatient([FromBody] PatientProfileRequest patientRequest)
    {
        var createdPatient = await _patientProfileService.CreateAsync(patientRequest);
        return CreatedAtAction(nameof(CreatePatient), new { id = createdPatient.PatientId }, createdPatient);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdatePatient(int id, [FromBody] PatientProfileUpdateRequest patientRequest)
    {
        if (!await _patientProfileService.VerifyRequestAsync(id))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                detail: $"The update request is not valid for patient {id}.", title: "Bad Request");
        }
        try
        {
            if (!await _patientProfileService.UpdateAsync(id, patientRequest))
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest,
                    detail: $"Patient {id} could not be updated.", title: "Bad Request");
            }
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            // Domain-rule violation surfaced as a 400 (kept explicit — the global handler maps
            // InvalidOperationException to 500, not 400).
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: ex.Message, title: "Bad Request");
        }
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
            var totalPastDueAmount = patientPastDueSessions.Sum(s => s.Amount - s.Discount);
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
