using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Common;

namespace Neurocorp.Api.Web.Controllers;

// Schedule-matrix endpoint, split out of SessionsController (Chunk 4). Keeps the api/sessions route
// prefix so the URL is unchanged.
[ApiController]
[Route("api/sessions")]
public class ScheduleController : ControllerBase
{
    private readonly IScheduleMatrixService _scheduleMatrixService;

    public ScheduleController(IScheduleMatrixService scheduleMatrixService)
    {
        _scheduleMatrixService = scheduleMatrixService;
    }

    [HttpGet("{dateString}/schedule-matrix")]
    [ProducesResponseType(typeof(ScheduleMatrixResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetScheduleMatrix(string dateString, [FromQuery] int siteId)
    {
        var targetDate = RouteDates.ParseOrToday(dateString);
        var result = await _scheduleMatrixService.GetMatrixAsync(targetDate, siteId);
        return Ok(result);
    }
}
