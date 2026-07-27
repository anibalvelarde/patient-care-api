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
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AppointmentsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<SessionEvent>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllEventsForADate(string dateString)
    {
        var targetDate = RouteDates.ParseOrToday(dateString);
        var sessions = await _sessionEventHandler.GetAllByTargetDateAsync(targetDate);
        return Ok(sessions);
    }

    // WP-17 (D-4): the past-due session feed is a delinquency view — Patients.Delinquent.View
    // (AM/MGR; FD denied), consistent with the patient/therapist past-due endpoints.
    [HttpGet("pastdue")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsDelinquentView)]
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
        // WP-24 (closes WP-17 audit F1): booking a DISCOVERY-specialty session additionally
        // requires Patients.StartDiscovery (MGR/AM/FD as of hash 7ae3aa5e3274). Imperative, not
        // an [Authorize] policy: the endpoint is generic and a static policy can't see the
        // specialty — it must be resolved from the request first. Nothing is created on the
        // Forbid path.
        if (await _sessionEventHandler.IsDiscoveryRequestAsync(sessionRequest)
            && !User.HasPermission(Permissions.PatientsStartDiscovery))
        {
            return Forbid();
        }

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

        // WP-40 (BK-3): CHANGING the Discount Amount on a booked session requires
        // Sessions.Discount.Edit (AM/MGR + SYSADMIN wildcard) — the interim surface for ad-hoc
        // discounts. Imperative like the WP-24/WP-37 field gates: only a changed discount trips
        // it, so FD keeps editing non-money fields; nothing changes on the Forbid path. The
        // SENADIS floor + recompute live in the handler.
        var sessionOnFile = await _sessionEventHandler.GetByIdAsync(id);
        if (sessionOnFile is not null
            && sessionUpdateRequest.Discount != sessionOnFile.Discount
            && !User.HasPermission(Permissions.SessionsDiscountEdit))
        {
            return Forbid();
        }

        if (!await _sessionEventHandler.UpdateAsync(id, sessionUpdateRequest))
        {
            throw new ArgumentException($"Session {id} could not be updated.");
        }
        return NoContent();
    }

    // WP-17 (D-5): completed discovery sessions are a read of appointment data — Appointments.View
    // (AM/FD/MGR). Discovery-START is separately enforced on CreateSession above via
    // Patients.StartDiscovery, which FD also holds as of hash 7ae3aa5e3274 (WP-24 closed audit F1).
    [HttpGet("patient/{patientId}/discovery")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AppointmentsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<DiscoverySessionSummary>))]
    public async Task<IActionResult> GetCompletedDiscoverySessions(int patientId)
    {
        var sessions = await _sessionEventHandler.GetCompletedDiscoverySessionsAsync(patientId);
        return Ok(sessions);
    }
}
