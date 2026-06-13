using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Authorization;

namespace Neurocorp.Api.Web.Controllers;

// Appointment booking lifecycle, split out of SessionsController (Chunk 4). Keeps the api/sessions
// route prefix so all URLs are unchanged. Errors bubble to the global ProblemDetails handler (3A).
[ApiController]
[Route("api/sessions")]
public class BookingController : ControllerBase
{
    private readonly IBookingService _bookingService;

    public BookingController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    // WP-17 access-control (decision D-1): intentionally authenticated-only — appointment-status
    // reference data for dropdowns that every role needs; not matrix-gated. Stays in
    // conformance-baseline.txt. See planning/active/wp-17b2-endpoint-claim-map.md.
    [HttpGet("statuses")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<LookupItem>))]
    public async Task<IActionResult> GetStatuses()
    {
        var statuses = await _bookingService.GetStatusesAsync();
        return Ok(statuses);
    }

    // WP-17 (D-2): Appointments.Book is the appointment WRITE capability — create/edit plus every
    // lifecycle transition (confirm/cancel/complete/no-show/check-in/start-therapy). Reassign and
    // ProviderAmount visibility are deliberately separate claims.
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AppointmentsBook)]
    [HttpPut("{id}/confirm")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SessionEvent))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmAppointment(int id, [FromBody] ConfirmationRequest request)
    {
        var result = await _bookingService.ConfirmAppointmentAsync(id, request);
        return Ok(result);
    }

    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AppointmentsBook)]
    [HttpPut("{id}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SessionEvent))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CancelAppointment(int id, [FromBody] CancelRequest request)
    {
        var result = await _bookingService.CancelAppointmentAsync(id, request.Reason);
        return Ok(result);
    }

    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AppointmentsBook)]
    [HttpPut("{id}/complete")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SessionEvent))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CompleteAppointment(int id)
    {
        var result = await _bookingService.UpdateStatusAsync(id, 4); // Completed
        return Ok(result);
    }

    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AppointmentsBook)]
    [HttpPut("{id}/noshow")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SessionEvent))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> NoShowAppointment(int id)
    {
        var result = await _bookingService.UpdateStatusAsync(id, 5); // NoShow
        return Ok(result);
    }

    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AppointmentsBook)]
    [HttpPut("{id}/checkin")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SessionEvent))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CheckInAppointment(int id)
    {
        var result = await _bookingService.UpdateStatusAsync(id, 6); // CheckedIn
        return Ok(result);
    }

    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AppointmentsBook)]
    [HttpPut("{id}/start-therapy")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SessionEvent))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartTherapy(int id)
    {
        var result = await _bookingService.UpdateStatusAsync(id, 7); // InTherapy
        return Ok(result);
    }

    [HttpGet("unconfirmed")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AppointmentsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<SessionEvent>))]
    public async Task<IActionResult> GetUnconfirmed([FromQuery] string? from = null, [FromQuery] string? to = null, [FromQuery] int days = 7)
    {
        DateOnly fromDate, toDate;
        if (from != null || to != null)
        {
            fromDate = from != null ? DateOnly.Parse(from) : DateOnly.FromDateTime(DateTime.UtcNow);
            toDate = to != null ? DateOnly.Parse(to) : fromDate.AddDays(7);
        }
        else
        {
            if (days > 30) days = 30;
            fromDate = DateOnly.FromDateTime(DateTime.UtcNow);
            toDate = fromDate.AddDays(days);
        }
        var sessions = await _bookingService.GetUnconfirmedAsync(fromDate, toDate);
        return Ok(sessions);
    }

    [HttpGet("upcoming")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AppointmentsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<SessionEvent>))]
    public async Task<IActionResult> GetUpcoming([FromQuery] string? from = null, [FromQuery] string? to = null, [FromQuery] int days = 7)
    {
        DateOnly fromDate, toDate;
        if (from != null || to != null)
        {
            fromDate = from != null ? DateOnly.Parse(from) : DateOnly.FromDateTime(DateTime.UtcNow);
            toDate = to != null ? DateOnly.Parse(to) : fromDate.AddDays(7);
        }
        else
        {
            if (days > 30) days = 30;
            fromDate = DateOnly.FromDateTime(DateTime.UtcNow);
            toDate = fromDate.AddDays(days);
        }
        var sessions = await _bookingService.GetUpcomingAsync(fromDate, toDate);
        return Ok(sessions);
    }

    [HttpGet("{id}/confirmations")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AppointmentsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<ConfirmationRecord>))]
    public async Task<IActionResult> GetConfirmations(int id)
    {
        var confirmations = await _bookingService.GetConfirmationsAsync(id);
        return Ok(confirmations);
    }
}
