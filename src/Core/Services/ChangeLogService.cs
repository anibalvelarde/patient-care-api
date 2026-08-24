using System.Globalization;
using System.Text.Json;
using Neurocorp.Api.Core.BusinessObjects.ChangeLog;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

/// <summary>
/// WP-54B change-log read side. The controller validates raw query params (window defaults/caps,
/// groupBy/action parsing, paging clamp) and hands this a normalized <see cref="ChangeLogQuery"/>;
/// this class orchestrates the repository, batches display-name resolution (WP-31 IUserNameResolver),
/// and rolls the repository's per-day buckets up into the requested period in pure C#.
/// </summary>
public class ChangeLogService : IChangeLogService
{
    /// <summary>WP-30 hygiene: distinct writers ≈ operator accounts (~10 today); cap the by-user block.</summary>
    public const int ByUserCap = 50;

    /// <summary>Beyond this many period buckets the trend strip is omitted (window cap keeps day ≤366).</summary>
    public const int MaxPeriodBuckets = 372;

    private const string SystemName = AuditInfo.SystemActor;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IChangeLogRepository _repo;
    private readonly IUserNameResolver _names;

    public ChangeLogService(IChangeLogRepository repo, IUserNameResolver names)
    {
        _repo = repo;
        _names = names;
    }

    public IReadOnlyList<ChangeLogEntityTypeInfo> GetEntityTypes() => _repo.EntityTypes();

    public async Task<ChangeLogPurgeResult> PurgeAsync(int olderThanDays, bool confirm)
        => await _repo.PurgeAsync(olderThanDays, confirm, DateTime.UtcNow);

    public async Task<PagedResult<ChangeLogEntry>> ListAsync(ChangeLogQuery query)
    {
        var page = await _repo.ListAsync(query);

        var ids = page.Items.Select(e => e.UserId).Where(id => id != 0).Distinct().ToList();
        var names = ids.Count > 0
            ? await _names.ResolveAsync(ids)
            : new Dictionary<int, string>();

        var items = page.Items.Select(e => new ChangeLogEntry
        {
            Id = e.Id,
            OccurredAtUtc = e.OccurredAtUtc,
            User = new ChangeLogUser
            {
                UserId = e.UserId,
                DisplayName = names.TryGetValue(e.UserId, out var n) ? n : SystemName,
            },
            EntityType = e.EntityType,
            EntityId = e.EntityId,
            EntityLabel = e.EntityLabel,
            Action = e.Action.ToString(),
            ChangedFields = DeserializeFields(e.Changes),
            CorrelationId = e.CorrelationId,
        }).ToList();

        return new PagedResult<ChangeLogEntry>
        {
            Items = items,
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount,
        };
    }

    public async Task<ChangeLogSummary> GetSummaryAsync(ChangeLogQuery query)
    {
        var totals = await _repo.TotalsAsync(query);
        var byUser = await _repo.ByUserAsync(query, ByUserCap);
        var byEntityType = await _repo.ByEntityTypeAsync(query);

        // Resolve by-user display names in one batched query.
        var ids = byUser.Select(u => u.UserId).Where(id => id != 0).Distinct().ToList();
        var names = ids.Count > 0 ? await _names.ResolveAsync(ids) : new Dictionary<int, string>();
        foreach (var u in byUser)
        {
            u.DisplayName = names.TryGetValue(u.UserId, out var n) ? n : SystemName;
        }

        var (byPeriod, omitted) = await BuildByPeriodAsync(query);

        return new ChangeLogSummary
        {
            FromUtc = query.FromUtc ?? default,
            ToUtc = query.ToUtc ?? default,
            GroupBy = query.GroupBy,
            TzOffsetMinutes = query.TzOffsetMinutes,
            Totals = totals,
            ByUser = byUser,
            ByEntityType = byEntityType,
            ByPeriod = byPeriod,
            ByPeriodOmitted = omitted,
        };
    }

    private async Task<(IReadOnlyList<ChangeLogPeriodCount> Buckets, bool Omitted)> BuildByPeriodAsync(ChangeLogQuery query)
    {
        // Observer-local window bounds (the same offset the repository buckets by).
        var localFrom = (query.FromUtc ?? default).AddMinutes(query.TzOffsetMinutes);
        var localTo = (query.ToUtc ?? default).AddMinutes(query.TzOffsetMinutes);

        var periods = EnumeratePeriods(localFrom, localTo, query.GroupBy).ToList();
        if (periods.Count > MaxPeriodBuckets)
        {
            return (Array.Empty<ChangeLogPeriodCount>(), true);
        }

        var days = await _repo.ByDayAsync(query);

        // index each day into its period key
        var byKey = new Dictionary<string, ChangeLogPeriodCount>();
        foreach (var p in periods)
        {
            byKey[p.Key] = new ChangeLogPeriodCount
            {
                PeriodStart = p.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Label = p.Label,
            };
        }

        foreach (var d in days)
        {
            var key = ChangeLogPeriods.Key(d.LocalDay, query.GroupBy);
            if (byKey.TryGetValue(key, out var bucket))
            {
                bucket.Inserts += d.Inserts;
                bucket.Updates += d.Updates;
                bucket.Deletes += d.Deletes;
            }
        }

        foreach (var b in byKey.Values)
        {
            b.Total = b.Inserts + b.Updates + b.Deletes;
        }

        return (periods.Select(p => byKey[p.Key]).ToList(), false);
    }

    private static IReadOnlyList<string> DeserializeFields(string? changesJson)
    {
        if (string.IsNullOrWhiteSpace(changesJson))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<string>>(changesJson, JsonOpts) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ---- period math (pure; unit-tested) ---------------------------------------

    private readonly record struct PeriodSpec(string Key, DateTime Start, string Label);

    private static IEnumerable<PeriodSpec> EnumeratePeriods(DateTime localFrom, DateTime localTo, string groupBy)
    {
        // Walk from the period covering localFrom to the period covering localTo, inclusive.
        var cursor = ChangeLogPeriods.Start(localFrom.Date, groupBy);
        var end = ChangeLogPeriods.Start(localTo.Date, groupBy);
        // Guard against a pathological window producing an unbounded loop.
        var guard = 0;
        while (cursor <= end && guard++ <= MaxPeriodBuckets + 1)
        {
            yield return new PeriodSpec(ChangeLogPeriods.Key(cursor, groupBy), cursor, ChangeLogPeriods.Label(cursor, groupBy));
            cursor = ChangeLogPeriods.Next(cursor, groupBy);
        }
    }

}
