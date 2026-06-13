using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Authorization;
using Neurocorp.Api.Web.Common;

namespace Neurocorp.Api.Web.Controllers;

// Core session-event endpoints. Booking lifecycle, schedule matrix, and session-payment queries
// were split out of this former god controller (Chunk 4) into BookingController,
// ScheduleController, and SessionPaymentsController — all sharing the api/sessions route prefix so
// existing URLs are unchanged.
[ApiController]
[Route("api/sessions")]
public class SessionsController : ControllerBase
{
    private readonly IHandleSessionEvent _sessionEventHandler;

    public SessionsController(IHandleSessionEvent handler)
    {
        _sessionEventHandler = handler;
    }

    [HttpGet("{dateString}/all")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<SessionEvent>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllEventsForADate(string dateString)
    {
        var targetDate = RouteDates.ParseOrToday(dateString);
        var sessions = await _sessionEventHandler.GetAllByTargetDateAsync(targetDate);
        return Ok(sessions);
    }

    [HttpGet("pastdue")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<SessionEvent>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllPastDueSessionEvents()
    {
        var sessions = await _sessionEventHandler.GetAllPastDueAsync();
        return Ok(sessions);
    }

    // WP-17 (D-2): a session is an appointment — create/edit gated by Appointments.Book.
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AppointmentsBook)]
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateSession([FromBody] SessionEventRequest sessionRequest)
    {
        var createdSession = await _sessionEventHandler.CreateAsync(sessionRequest);
        return CreatedAtAction(nameof(CreateSession), new { id = createdSession.SessionId }, createdSession);
    }

    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AppointmentsBook)]
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateSession(int id, [FromBody] SessionEventUpdateRequest sessionUpdateRequest)
    {
        if (!await _sessionEventHandler.VerifyRequestAsync(id, sessionUpdateRequest))
        {
            throw new ArgumentException($"The update request is not valid for session {id}.");
        }
        if (!await _sessionEventHandler.UpdateAsync(id, sessionUpdateRequest))
        {
            throw new ArgumentException($"Session {id} could not be updated.");
        }
        return NoContent();
    }

    [HttpGet("patient/{patientId}/discovery")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<DiscoverySessionSummary>))]
    public async Task<IActionResult> GetCompletedDiscoverySessions(int patientId)
    {
        var sessions = await _sessionEventHandler.GetCompletedDiscoverySessionsAsync(patientId);
        return Ok(sessions);
    }
}
