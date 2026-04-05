using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Payments;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SessionsController : ControllerBase
{
    private readonly ILogger<SessionsController> _logger;
    private readonly IHandleSessionEvent _sessionEventHandler;
    private readonly IPaymentRecordService _paymentService;
    private readonly IBookingService _bookingService;

    public SessionsController(
        ILogger<SessionsController> loger,
        IHandleSessionEvent handler,
        IPaymentRecordService paymentService,
        IBookingService bookingService)
    {
        _logger = loger;
        _sessionEventHandler = handler;
        _paymentService = paymentService;
        _bookingService = bookingService;
    }

    [HttpGet("{dateString}/all")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<SessionEvent>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllEventsForADate(string dateString)
    {
        var targetDate = ConvertStringToDateOnly(dateString);
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

    [HttpPost]
    public async Task<IActionResult> CreateSession([FromBody] SessionEventRequest sessionRequest)
    {
        try
        {
            var createdSession = await _sessionEventHandler.CreateAsync(sessionRequest);
            return CreatedAtAction(nameof(CreateSession), new { id = createdSession.SessionId }, createdSession);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateSession(int id, [FromBody] SessionEventUpdateRequest sessionUpdateRequest)
    {
        if(await _sessionEventHandler.VerifyRequestAsync(id, sessionUpdateRequest))
        {
            var updateResult = await _sessionEventHandler.UpdateAsync(id, sessionUpdateRequest);
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

    [HttpGet("{id}/payments")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<SessionPaymentDetail>))]
    public async Task<IActionResult> GetSessionPayments(int id)
    {
        var payments = await _paymentService.GetPaymentsForSessionAsync(id);
        return Ok(payments);
    }

    // --- Appointment Booking Endpoints ---

    [HttpGet("statuses")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<LookupItem>))]
    public async Task<IActionResult> GetStatuses()
    {
        var statuses = await _bookingService.GetStatusesAsync();
        return Ok(statuses);
    }

    [HttpPut("{id}/confirm")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SessionEvent))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmAppointment(int id, [FromBody] ConfirmationRequest request)
    {
        try
        {
            var result = await _bookingService.ConfirmAppointmentAsync(id, request);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SessionEvent))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CancelAppointment(int id, [FromBody] CancelRequest request)
    {
        try
        {
            var result = await _bookingService.CancelAppointmentAsync(id, request.Reason);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id}/complete")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SessionEvent))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CompleteAppointment(int id)
    {
        try
        {
            var result = await _bookingService.UpdateStatusAsync(id, 4); // Completed
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id}/noshow")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SessionEvent))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> NoShowAppointment(int id)
    {
        try
        {
            var result = await _bookingService.UpdateStatusAsync(id, 5); // NoShow
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id}/checkin")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SessionEvent))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CheckInAppointment(int id)
    {
        try
        {
            var result = await _bookingService.UpdateStatusAsync(id, 6); // CheckedIn
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id}/start-therapy")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SessionEvent))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartTherapy(int id)
    {
        try
        {
            var result = await _bookingService.UpdateStatusAsync(id, 7); // InTherapy
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("unconfirmed")]
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
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<ConfirmationRecord>))]
    public async Task<IActionResult> GetConfirmations(int id)
    {
        var confirmations = await _bookingService.GetConfirmationsAsync(id);
        return Ok(confirmations);
    }

    public static DateOnly ConvertStringToDateOnly(string dateString)
    {
        if (DateOnly.TryParse(dateString, out DateOnly date))
        {
            return date;
        }
        else
        {
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }
    }

}