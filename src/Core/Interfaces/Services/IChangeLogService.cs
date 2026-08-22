using Neurocorp.Api.Core.BusinessObjects.ChangeLog;
using Neurocorp.Api.Core.BusinessObjects.Common;

namespace Neurocorp.Api.Core.Interfaces.Services;

/// <summary>
/// WP-54B change-log read side. Owns window defaults/caps, groupBy/action parsing, the day → period
/// rollup, and display-name batching. All raw query params arrive as nullable/string and are
/// validated here (400 via the house error convention on bad input).
/// </summary>
public interface IChangeLogService
{
    Task<ChangeLogSummary> GetSummaryAsync(ChangeLogQuery query);

    Task<PagedResult<ChangeLogEntry>> ListAsync(ChangeLogQuery query);

    /// <summary>The static set of audited entity types (from the capture registry) — stable filter chips.</summary>
    IReadOnlyList<ChangeLogEntityTypeInfo> GetEntityTypes();

    /// <summary>On-demand purge (D13): dry-run by default; <paramref name="confirm"/>=true deletes.</summary>
    Task<ChangeLogPurgeResult> PurgeAsync(int olderThanDays, bool confirm);
}
