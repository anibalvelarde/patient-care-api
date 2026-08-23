using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.BusinessObjects.ChangeLog;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Data.ChangeLog;

namespace Neurocorp.Api.Infrastructure.Repositories;

/// <summary>
/// WP-54B read side. Every query runs over the same filtered set; grouping is pushed to the DB
/// (WP-29 lesson — no N+1, no materialising the window). The per-day grouping applies the observer
/// tz offset in the query so buckets align to the viewer's calendar; the service rolls days up into
/// the requested period.
/// </summary>
public class ChangeLogRepository : IChangeLogRepository
{
    private readonly ApplicationDbContext _dbContext;

    public ChangeLogRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private IQueryable<EntityChangeLog> Filtered(ChangeLogQuery q)
    {
        var query = _dbContext.Set<EntityChangeLog>().AsNoTracking();
        if (q.FromUtc is { } from)
        {
            query = query.Where(e => e.OccurredAtUtc >= from);
        }
        if (q.ToUtc is { } to)
        {
            query = query.Where(e => e.OccurredAtUtc < to);
        }
        if (q.UserId is { } userId)
        {
            query = query.Where(e => e.UserId == userId);
        }
        if (!string.IsNullOrWhiteSpace(q.EntityType))
        {
            query = query.Where(e => e.EntityType == q.EntityType);
        }
        if (!string.IsNullOrWhiteSpace(q.EntityId))
        {
            query = query.Where(e => e.EntityId == q.EntityId);
        }
        if (q.Action is { } action)
        {
            query = query.Where(e => e.Action == action);
        }
        if (!string.IsNullOrWhiteSpace(q.CorrelationId))
        {
            query = query.Where(e => e.CorrelationId == q.CorrelationId);
        }
        return query;
    }

    public async Task<PagedResult<EntityChangeLog>> ListAsync(ChangeLogQuery query)
    {
        var filtered = Filtered(query);
        var total = await filtered.CountAsync();
        var items = await filtered
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => e.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        return new PagedResult<EntityChangeLog>
        {
            Items = items,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = total,
        };
    }

    public async Task<ChangeLogTotals> TotalsAsync(ChangeLogQuery query)
    {
        var filtered = Filtered(query);
        var inserts = await filtered.CountAsync(e => e.Action == ChangeAction.Insert);
        var updates = await filtered.CountAsync(e => e.Action == ChangeAction.Update);
        var deletes = await filtered.CountAsync(e => e.Action == ChangeAction.Delete);
        var distinctUsers = await filtered.Select(e => e.UserId).Distinct().CountAsync();
        var distinctEntities = await filtered
            .Select(e => new { e.EntityType, e.EntityId }).Distinct().CountAsync();

        return new ChangeLogTotals
        {
            Inserts = inserts,
            Updates = updates,
            Deletes = deletes,
            Total = inserts + updates + deletes,
            DistinctUsers = distinctUsers,
            DistinctEntities = distinctEntities,
        };
    }

    public async Task<IReadOnlyList<ChangeLogUserCount>> ByUserAsync(ChangeLogQuery query, int cap)
        => await Filtered(query)
            .GroupBy(e => e.UserId)
            .Select(g => new ChangeLogUserCount
            {
                UserId = g.Key,
                Inserts = g.Count(x => x.Action == ChangeAction.Insert),
                Updates = g.Count(x => x.Action == ChangeAction.Update),
                Deletes = g.Count(x => x.Action == ChangeAction.Delete),
                Total = g.Count(),
                LastOccurredAtUtc = g.Max(x => x.OccurredAtUtc),
            })
            .OrderByDescending(u => u.Total)
            .ThenBy(u => u.UserId)
            .Take(cap)
            .ToListAsync();

    public async Task<IReadOnlyList<ChangeLogEntityTypeCount>> ByEntityTypeAsync(ChangeLogQuery query)
        => await Filtered(query)
            .GroupBy(e => e.EntityType)
            .Select(g => new ChangeLogEntityTypeCount
            {
                EntityType = g.Key,
                Inserts = g.Count(x => x.Action == ChangeAction.Insert),
                Updates = g.Count(x => x.Action == ChangeAction.Update),
                Deletes = g.Count(x => x.Action == ChangeAction.Delete),
                Total = g.Count(),
                LastOccurredAtUtc = g.Max(x => x.OccurredAtUtc),
            })
            .OrderByDescending(t => t.Total)
            .ThenBy(t => t.EntityType)
            .ToListAsync();

    public async Task<IReadOnlyList<ChangeLogDayCount>> ByDayAsync(ChangeLogQuery query)
    {
        var offset = query.TzOffsetMinutes;
        return await Filtered(query)
            .Select(e => new { LocalDay = e.OccurredAtUtc.AddMinutes(offset).Date, e.Action })
            .GroupBy(x => x.LocalDay)
            .Select(g => new ChangeLogDayCount
            {
                LocalDay = g.Key,
                Inserts = g.Count(x => x.Action == ChangeAction.Insert),
                Updates = g.Count(x => x.Action == ChangeAction.Update),
                Deletes = g.Count(x => x.Action == ChangeAction.Delete),
            })
            .ToListAsync();
    }

    public IReadOnlyList<ChangeLogEntityTypeInfo> EntityTypes()
        => ChangeLogRegistry.EntityTypes(_dbContext.Model);

    public async Task<ChangeLogPurgeResult> PurgeAsync(int olderThanDays, bool confirm, DateTime utcNow)
    {
        var cutoff = utcNow.AddDays(-olderThanDays);
        var toGo = _dbContext.Set<EntityChangeLog>().Where(e => e.OccurredAtUtc < cutoff);
        var matched = await toGo.LongCountAsync();

        long deleted = 0;
        if (confirm && matched > 0)
        {
            // Bulk delete — bypasses SaveChanges (and the capture interceptor) by design: the log is
            // being emptied, and the purge itself is never self-logged.
            deleted = await toGo.ExecuteDeleteAsync();
        }

        return new ChangeLogPurgeResult
        {
            OlderThanDays = olderThanDays,
            CutoffUtc = cutoff,
            Matched = matched,
            Deleted = confirm ? deleted : 0,
            DryRun = !confirm,
        };
    }
}
