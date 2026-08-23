using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.ChangeLog;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Authorization;

namespace Neurocorp.Api.Web.Controllers;

/// <summary>
/// WP-54B: SYSADMIN change-log read side + on-demand purge (contract: _contracts/admin-change-log-api.md).
/// Reads ride <c>Admin.ChangeLog.View</c>; the destructive purge rides a separate
/// <c>Admin.ChangeLog.Purge</c> (so a View grant can never purge). Field NAMES only — no values (G2).
/// This controller validates raw query params (window defaults/caps, groupBy/action parsing,
/// paging clamp) and hands the service a normalized query.
/// </summary>
[ApiController]
[Route("api/admin/change-log")]
public class AdminChangeLogController : ControllerBase
{
    private const int DefaultPageSize = 25;
    private const int MaxWindowDays = 366;
    private const int PurgeDefaultOlderThanDays = 1100;

    private static readonly IReadOnlySet<string> GroupByValues = ChangeLogPeriods.All;

    private readonly IChangeLogService _service;

    public AdminChangeLogController(IChangeLogService service)
    {
        _service = service;
    }

    /// <summary>Macro "single pane of glass": totals, by-user (lead), by-entity-type, and the period trend.</summary>
    [HttpGet("summary")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AdminChangeLogView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ChangeLogSummary))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int tzOffsetMinutes = 0,
        [FromQuery] string groupBy = ChangeLogPeriods.Day,
        [FromQuery] int? userId = null,
        [FromQuery] string? entityType = null,
        [FromQuery] string? action = null)
    {
        if (!TryBuildQuery(from, to, tzOffsetMinutes, groupBy, userId, entityType, null, action, 1, DefaultPageSize, out var query, out var error))
        {
            return BadRequest(new { error });
        }
        return Ok(await _service.GetSummaryAsync(query));
    }

    /// <summary>Paged detail list, newest first (WP-30 rule / WP-21 envelope). The "double-click".</summary>
    [HttpGet]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AdminChangeLogView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PagedResult<ChangeLogEntry>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int? userId = null,
        [FromQuery] string? entityType = null,
        [FromQuery] string? entityId = null,
        [FromQuery] string? action = null,
        [FromQuery] string? correlationId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        if (!TryBuildQuery(from, to, 0, ChangeLogPeriods.Day, userId, entityType, entityId, action, page, pageSize, out var query, out var error))
        {
            return BadRequest(new { error });
        }
        query.CorrelationId = correlationId;
        return Ok(await _service.ListAsync(query));
    }

    /// <summary>Static list of audited entity types (stable filter chips even for an empty window).</summary>
    [HttpGet("entity-types")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AdminChangeLogView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IReadOnlyList<ChangeLogEntityTypeInfo>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetEntityTypes() => Ok(_service.GetEntityTypes());

    /// <summary>
    /// On-demand retention purge (D13). API-only — never wired into any UI. Dry-run by default; pass
    /// <paramref name="confirm"/>=true to delete. Rides <c>Admin.ChangeLog.Purge</c> (not View).
    /// </summary>
    [HttpDelete]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AdminChangeLogPurge)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ChangeLogPurgeResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Purge(
        [FromQuery] int olderThanDays = PurgeDefaultOlderThanDays,
        [FromQuery] bool confirm = false)
    {
        if (olderThanDays < 1)
        {
            return BadRequest(new { error = "olderThanDays must be >= 1." });
        }
        return Ok(await _service.PurgeAsync(olderThanDays, confirm));
    }

    // ---- query validation ------------------------------------------------------

    private bool TryBuildQuery(
        DateTimeOffset? from, DateTimeOffset? to, int tzOffsetMinutes, string groupBy,
        int? userId, string? entityType, string? entityId, string? action,
        int page, int pageSize,
        out ChangeLogQuery query, out string? error)
    {
        query = new ChangeLogQuery();
        error = null;

        var toUtc = to?.UtcDateTime ?? DateTime.UtcNow;
        var fromUtc = from?.UtcDateTime ?? toUtc.AddHours(-24);
        if (fromUtc > toUtc)
        {
            error = "'from' must be on or before 'to'.";
            return false;
        }
        if ((toUtc - fromUtc).TotalDays > MaxWindowDays)
        {
            error = $"The window may not exceed {MaxWindowDays} days.";
            return false;
        }

        if (Math.Abs(tzOffsetMinutes) > 14 * 60)
        {
            error = "tzOffsetMinutes is out of range.";
            return false;
        }

        var group = (groupBy ?? ChangeLogPeriods.Day).Trim().ToLowerInvariant();
        if (!GroupByValues.Contains(group))
        {
            error = "groupBy must be one of: day, week, month, quarter, year.";
            return false;
        }

        ChangeAction? parsedAction = null;
        if (!string.IsNullOrWhiteSpace(action))
        {
            if (!Enum.TryParse<ChangeAction>(action.Trim(), ignoreCase: true, out var a)
                || !Enum.IsDefined(a))
            {
                error = "action must be one of: Insert, Update, Delete.";
                return false;
            }
            parsedAction = a;
        }

        if (!string.IsNullOrWhiteSpace(entityType)
            && !_service.GetEntityTypes().Any(t => string.Equals(t.EntityType, entityType, StringComparison.Ordinal)))
        {
            error = $"Unknown entityType '{entityType}'.";
            return false;
        }

        var (safePage, safeSize) = PagingParams.Clamp(page, pageSize, DefaultPageSize);

        query = new ChangeLogQuery
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            TzOffsetMinutes = tzOffsetMinutes,
            GroupBy = group,
            UserId = userId,
            EntityType = string.IsNullOrWhiteSpace(entityType) ? null : entityType,
            EntityId = string.IsNullOrWhiteSpace(entityId) ? null : entityId,
            Action = parsedAction,
            Page = safePage,
            PageSize = safeSize,
        };
        return true;
    }
}
