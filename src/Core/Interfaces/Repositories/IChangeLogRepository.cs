using Neurocorp.Api.Core.BusinessObjects.ChangeLog;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

/// <summary>
/// WP-54B read side. All queries are the same filtered set (from/to/user/entityType/entityId/action/
/// correlationId); the service layer adds display-name resolution and the day → period rollup.
/// </summary>
public interface IChangeLogRepository
{
    /// <summary>Paged detail list, newest first (OccurredAtUtc DESC, Id DESC).</summary>
    Task<PagedResult<EntityChangeLog>> ListAsync(ChangeLogQuery query);

    /// <summary>Totals over the filtered set (counts + distinct users/entities).</summary>
    Task<ChangeLogTotals> TotalsAsync(ChangeLogQuery query);

    /// <summary>Counts grouped by acting user (DisplayName filled by the service), sorted by total desc, capped.</summary>
    Task<IReadOnlyList<ChangeLogUserCount>> ByUserAsync(ChangeLogQuery query, int cap);

    /// <summary>Counts grouped by entity type, sorted by total desc.</summary>
    Task<IReadOnlyList<ChangeLogEntityTypeCount>> ByEntityTypeAsync(ChangeLogQuery query);

    /// <summary>The audited entity types from the EF model (capture registry) — stable filter chips.</summary>
    IReadOnlyList<ChangeLogEntityTypeInfo> EntityTypes();

    /// <summary>Per-day counts in the observer-local calendar (offset applied); the service rolls these into periods.</summary>
    Task<IReadOnlyList<ChangeLogDayCount>> ByDayAsync(ChangeLogQuery query);

    /// <summary>
    /// On-demand purge (D13). Counts rows older than the cutoff; deletes them only when
    /// <paramref name="confirm"/> is true. <paramref name="utcNow"/> is injected for testability.
    /// </summary>
    Task<ChangeLogPurgeResult> PurgeAsync(int olderThanDays, bool confirm, DateTime utcNow);
}
