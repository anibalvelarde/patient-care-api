using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SessionsController : ControllerBase
{
    private readonly ILogger<SessionsController> _logger;
    private readonly IHandleSessionEvent _sessionEventHandler;

    public SessionsController(ILogger<SessionsController> loger, IHandleSessionEvent handler)
    {
        _logger = loger;
        _sessionEventHandler = handler;
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