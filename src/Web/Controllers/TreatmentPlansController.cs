using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Authorization;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/treatment-plans")]
public class TreatmentPlansController : ControllerBase
{
    private readonly ILogger<TreatmentPlansController> _logger;
    private readonly ITreatmentPlanService _service;
    private readonly IBulkSchedulingService _schedulingService;

    public TreatmentPlansController(
        ILogger<TreatmentPlansController> logger,
        ITreatmentPlanService service,
        IBulkSchedulingService schedulingService)
    {
        _logger = logger;
        _service = service;
        _schedulingService = schedulingService;
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.TreatmentPlansEdit)]
    [ProducesResponseType(typeof(TreatmentPlanProfile), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] TreatmentPlanRequest request)
    {
        var result = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    // WP-17 (D-7): treated as a plan read (TreatmentPlans.View, AM/FD/MGR) rather than tied to the
    // dashboard alerts widget (Dashboard.PlanAlerts) — same granted set; this is the intent.
    [HttpGet("active-summary")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.TreatmentPlansView)]
    [ProducesResponseType(typeof(IReadOnlyList<ActivePlanSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveSummary()
    {
        var results = await _service.GetActiveSummaryAsync();
        return Ok(results);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.TreatmentPlansView)]
    [ProducesResponseType(typeof(TreatmentPlanProfile), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("patient/{patientId}")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.TreatmentPlansView)]
    [ProducesResponseType(typeof(IReadOnlyList<TreatmentPlanProfile>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByPatient(int patientId)
    {
        var results = await _service.GetByPatientIdAsync(patientId);
        return Ok(results);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.TreatmentPlansEdit)]
    [ProducesResponseType(typeof(TreatmentPlanProfile), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(int id, [FromBody] TreatmentPlanRequest request)
    {
        var result = await _service.UpdateAsync(id, request);
        return Ok(result);
    }

    [HttpPut("{id}/activate")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.TreatmentPlansEdit)]
    [ProducesResponseType(typeof(TreatmentPlanProfile), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Activate(int id)
    {
        var result = await _service.ActivateAsync(id);
        return Ok(result);
    }

    [HttpPut("{id}/complete")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.TreatmentPlansEdit)]
    [ProducesResponseType(typeof(TreatmentPlanProfile), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Complete(int id)
    {
        var result = await _service.CompleteAsync(id);
        return Ok(result);
    }

    [HttpPut("{id}/cancel")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.TreatmentPlansEdit)]
    [ProducesResponseType(typeof(TreatmentPlanProfile), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Cancel(int id)
    {
        var result = await _service.CancelAsync(id);
        return Ok(result);
    }

    // WP-17 (D-6): claim deliberately UNDECIDED — pending human discussion (TreatmentPlans.Edit vs
    // Schedule.Book). Stays authenticated-only in conformance-baseline.txt until D-6 is resolved.
    [HttpPost("{id}/schedule")]
    [ProducesResponseType(typeof(BulkScheduleResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Schedule(int id, [FromBody] BulkScheduleRequest request)
    {
        var result = await _schedulingService.ScheduleAsync(id, request);
        return Ok(result);
    }

    [HttpGet("{id}/sessions")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.TreatmentPlansView)]
    [ProducesResponseType(typeof(IReadOnlyList<PlanSessionInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSessions(int id)
    {
        try
        {
            var result = await _schedulingService.GetSessionsAsync(id);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            // This path treats an invalid plan id as "not found" (404) rather than a 400.
            return Problem(statusCode: StatusCodes.Status404NotFound, detail: ex.Message, title: "Not Found");
        }
    }

    [HttpGet("{id}/progress")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.TreatmentPlansView)]
    [ProducesResponseType(typeof(PlanProgressSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProgress(int id)
    {
        try
        {
            var result = await _schedulingService.GetProgressAsync(id);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, detail: ex.Message, title: "Not Found");
        }
    }
}
