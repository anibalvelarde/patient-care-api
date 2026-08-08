using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Authorization;

namespace Neurocorp.Api.Web.Controllers;

/// <summary>
/// WP-49 (BR3/BR4): the late-fee batch and the fee waiver.
///
/// Claim split is deliberate. The PREVIEW is a receivables read with the same audience as every
/// other delinquency read, so it rides <c>Patients.Delinquent.View</c> — an AM can look at what
/// is owed but cannot fire the charge. APPLY and WAIVE both require <c>Sessions.Fee.Manage</c>
/// (SYSADMIN + MGR), one claim covering both because splitting them would let a role levy fees
/// it could not forgive, inverting BR4.
/// </summary>
[ApiController]
[Route("api/sessions")]
public class SessionFeesController : ControllerBase
{
    private readonly ISessionFeeService _sessionFeeService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<SessionFeesController> _logger;

    public SessionFeesController(
        ILogger<SessionFeesController> logger,
        ISessionFeeService sessionFeeService,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _sessionFeeService = sessionFeeService;
        _currentUserService = currentUserService;
    }

    /// <summary>What the batch would charge as of a given date. Read-only; charges nothing.</summary>
    [HttpGet("late-fees/preview")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsDelinquentView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LateFeePreviewResult))]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> PreviewLateFees([FromQuery] DateOnly? asOf)
    {
        var preview = await _sessionFeeService.PreviewLateFeesAsync(asOf);
        return Ok(preview);
    }

    /// <summary>
    /// Charge the selected sessions. Mirrors <c>POST /api/service-payments/batch</c>.
    /// Eligibility is re-derived per session at write time, so the response reports both what
    /// was applied and what was skipped and why.
    /// </summary>
    [HttpPost("late-fees/batch")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.SessionsFeeManage)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ApplyLateFeesResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ApplyLateFees([FromBody] ApplyLateFeesRequest request)
    {
        var actingUserId = _currentUserService.UserId ?? 0;
        _logger.LogInformation("User {UserId} firing the late-fee batch over {Count} session(s)",
            actingUserId, request.SessionIds?.Count ?? 0);

        var result = await _sessionFeeService.ApplyLateFeesAsync(request, actingUserId);
        return Ok(result);
    }

    /// <summary>
    /// Forgive a late and/or no-show fee. Mirrors <c>POST /api/service-payments/{id}/reverse</c>:
    /// mandatory reason, cannot re-waive, original preserved in the audit marker.
    /// </summary>
    [HttpPost("{id}/waive-fee")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.SessionsFeeManage)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WaiveFeeResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> WaiveFee(int id, [FromBody] WaiveFeeRequest request)
    {
        var actingUserId = _currentUserService.UserId ?? 0;
        _logger.LogInformation("User {UserId} waiving fee on session {SessionId}", actingUserId, id);

        var result = await _sessionFeeService.WaiveFeeAsync(id, request, actingUserId);
        return Ok(result);
    }
}
