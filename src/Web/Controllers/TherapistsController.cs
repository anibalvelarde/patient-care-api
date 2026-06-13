using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Neurocorp.Api.Core.BusinessObjects.Statements;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Web.Authorization;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TherapistsController : ControllerBase
{
    private readonly ITherapistProfileService _therapistProfileService;
    private readonly IHandleSessionEvent _sessionEventHandler;
    private readonly ITherapistStatementService _therapistStatementService;
    private readonly ILogger<TherapistsController> _logger;

    public TherapistsController(
        ILogger<TherapistsController> logger,
        ITherapistProfileService therapistProfileService,
        IHandleSessionEvent sesssionEventHandler,
        ITherapistStatementService therapistStatementService)
    {
        _therapistProfileService = therapistProfileService;
        _sessionEventHandler = sesssionEventHandler;
        _therapistStatementService = therapistStatementService;
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
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.TherapistsDelinquentView)]
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
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(TherapistProfile))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateTherapist([FromBody] TherapistProfileRequest therapistRequest)
    {
        var createdTherapist = await _therapistProfileService.CreateAsync(therapistRequest);
        return CreatedAtAction(nameof(GetTherapist), new { id = createdTherapist.TherapistId }, createdTherapist);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateTherapist(int id, [FromBody] TherapistProfileUpdateRequest therapistRequest)
    {
        if (!await _therapistProfileService.VerifyRequestAsync(id))
        {
            throw new ArgumentException($"The update request is not valid for therapist {id}.");
        }
        if (!await _therapistProfileService.UpdateAsync(id, therapistRequest))
        {
            throw new ArgumentException($"Therapist {id} could not be updated.");
        }
        return NoContent();
    }

    [HttpGet("{id}/statement")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.StatementsTherapistView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TherapistStatement))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetStatement(int id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        if (from.HasValue && to.HasValue && to.Value < from.Value)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                detail: "Query parameter 'to' must be on or after 'from'.", title: "Bad Request");
        }

        var statement = await _therapistStatementService.GetStatementAsync(id, from, to);
        if (statement == null) return NotFound();
        return Ok(statement);
    }

    private async Task<TherapistPastDueInfo> PackagePastDueInfoAsync(int therapistId)
    {
        var therapist = await _therapistProfileService.GetByIdAsync(therapistId);
        if (therapist is not null)
        {
            var pastDueSessions = await _sessionEventHandler.GetAllPastDueAsync();
            var patientPastDueSessions = pastDueSessions
                .Where(s => s.TherapistId.Equals(therapistId))
                .Select(s => s);
            var totalPastDueAmount = patientPastDueSessions.Sum(s => s.Amount - s.Discount);
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
