using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.BusinessObjects.ChangeLog;

/// <summary>The acting user on a change-log row. DisplayName resolved via IUserNameResolver; 0/unknown → "System".</summary>
public class ChangeLogUser
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = AuditInfo.SystemActor;
}

/// <summary>One change-log entry as returned by the detail list. Field NAMES only — never values (G2).</summary>
public class ChangeLogEntry
{
    public long Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public ChangeLogUser User { get; set; } = new();
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? EntityLabel { get; set; }
    public string Action { get; set; } = string.Empty;
    public IReadOnlyList<string> ChangedFields { get; set; } = [];
    public string? CorrelationId { get; set; }
}

public class ChangeLogTotals
{
    public int Inserts { get; set; }
    public int Updates { get; set; }
    public int Deletes { get; set; }
    public int Total { get; set; }
    public int DistinctUsers { get; set; }
    public int DistinctEntities { get; set; }
}

public class ChangeLogUserCount
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = AuditInfo.SystemActor;
    public int Inserts { get; set; }
    public int Updates { get; set; }
    public int Deletes { get; set; }
    public int Total { get; set; }
    public DateTime LastOccurredAtUtc { get; set; }
}

public class ChangeLogEntityTypeCount
{
    public string EntityType { get; set; } = string.Empty;
    public int Inserts { get; set; }
    public int Updates { get; set; }
    public int Deletes { get; set; }
    public int Total { get; set; }
    public DateTime LastOccurredAtUtc { get; set; }
}

/// <summary>One bucket of the trend strip. PeriodStart = ISO date of the bucket's first day in observer-local time.</summary>
public class ChangeLogPeriodCount
{
    public string PeriodStart { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Inserts { get; set; }
    public int Updates { get; set; }
    public int Deletes { get; set; }
    public int Total { get; set; }
}

/// <summary>
/// Per-day bucket in the OBSERVER's local calendar (repository output; the service rolls these up
/// into the requested period — day/week/month/quarter/year — in pure C#, which keeps the SQL to a
/// single ≤366-row day grouping regardless of granularity).
/// </summary>
public class ChangeLogDayCount
{
    /// <summary>Midnight of the observer-local day this bucket covers (date only; time is 00:00).</summary>
    public DateTime LocalDay { get; set; }
    public int Inserts { get; set; }
    public int Updates { get; set; }
    public int Deletes { get; set; }
}

/// <summary>The "single pane of glass" — led by the by-user view (G1).</summary>
public class ChangeLogSummary
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public string GroupBy { get; set; } = "day";
    public int TzOffsetMinutes { get; set; }
    public ChangeLogTotals Totals { get; set; } = new();
    public IReadOnlyList<ChangeLogUserCount> ByUser { get; set; } = [];
    public IReadOnlyList<ChangeLogEntityTypeCount> ByEntityType { get; set; } = [];
    public IReadOnlyList<ChangeLogPeriodCount> ByPeriod { get; set; } = [];
    public bool ByPeriodOmitted { get; set; }
}

public class ChangeLogEntityTypeInfo
{
    public string EntityType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>Result of the on-demand retention purge (D13). Dry-run by default.</summary>
public class ChangeLogPurgeResult
{
    public int OlderThanDays { get; set; }
    public DateTime CutoffUtc { get; set; }
    public long Matched { get; set; }
    public long Deleted { get; set; }
    public bool DryRun { get; set; }
}

/// <summary>Parsed, validated query for the summary + list endpoints.</summary>
public class ChangeLogQuery
{
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public int TzOffsetMinutes { get; set; }
    public string GroupBy { get; set; } = "day";
    public int? UserId { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public ChangeAction? Action { get; set; }
    public string? CorrelationId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}
