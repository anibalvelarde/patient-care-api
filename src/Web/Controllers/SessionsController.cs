using Microsoft.AspNetCore.Mvc;
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

    public SessionsController(
        ILogger<SessionsController> loger,
        IHandleSessionEvent handler,
        IPaymentRecordService paymentService)
    {
        _logger = loger;
        _sessionEventHandler = handler;
        _paymentService = paymentService;
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
        var createdSession = await _sessionEventHandler.CreateAsync(sessionRequest);
        return CreatedAtAction(nameof(CreateSession), new { id = createdSession.SessionId }, createdSession);
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