using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neurocorp.Api.Core.Configurations;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Infrastructure.Data.ChangeLog;

/// <summary>
/// WP-54 capture. A scoped <see cref="SaveChangesInterceptor"/> that records one append-only
/// <see cref="EntityChangeLog"/> row per Added/Modified/Deleted mapped entity — field NAMES only,
/// never values (G2). Two-phase: snapshot in SavingChanges (so Modified/Deleted keys and the label
/// are read while the entity is still tracked), then in SavedChanges fill the generated Insert keys,
/// add the log rows, and save once more.
///
/// TRANSACTIONS: the MySQL provider uses a retrying execution strategy (EnableRetryOnFailure), which
/// forbids a user-initiated BeginTransaction outside CreateExecutionStrategy — so this interceptor
/// deliberately does NOT open its own transaction. The log-row save simply JOINS whatever ambient
/// transaction is open: multi-entity writes flow through EfUnitOfWork, so their log rows commit
/// atomically with the data; a bare SaveChanges logs best-effort (a failure loses only the log row,
/// never the business write — logging must never break the app; the kill-switch is the valve).
///
/// CHANGED FIELDS: for a Modified entry we list the IsModified scalar columns (minus the PK and the
/// D4 exclusion set). This is accurate for change-tracked edits and honestly over-reports for
/// attach-as-Modified writes (EF cannot know the subset there) — it never silently drops a change.
/// An Update whose only modified columns are excluded (e.g. a login touching LastLoginAt) logs nothing.
/// </summary>
public class ChangeLogInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IOptions<ChangeLogOptions> _options;
    private readonly ICurrentUserService? _currentUser;
    private readonly IRequestCorrelation? _correlation;
    private readonly ILogger<ChangeLogInterceptor>? _logger;

    // Per-save state (a scoped context saves sequentially, so single fields are safe here).
    private List<Pending>? _pending;
    private DateTime _occurredAtUtc;
    private int _userId;
    private string? _correlationId;
    private bool _writing; // re-entrancy guard for our own log-row save

    public ChangeLogInterceptor(
        IOptions<ChangeLogOptions> options,
        ICurrentUserService? currentUser = null,
        IRequestCorrelation? correlation = null,
        ILogger<ChangeLogInterceptor>? logger = null)
    {
        _options = options;
        _currentUser = currentUser;
        _correlation = correlation;
        _logger = logger;
    }

    // ---- phase 1: snapshot (before the data is written) ------------------------

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    // ---- phase 2: write the log rows (after the data is written) ----------------

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        var rows = BuildRows(eventData.Context);
        if (rows is not null)
        {
            _writing = true;
            try
            {
                eventData.Context!.Set<EntityChangeLog>().AddRange(rows);
                eventData.Context.SaveChanges();
            }
            catch (Exception ex)
            {
                Fail(eventData.Context, rows, ex);
            }
            finally
            {
                _writing = false;
            }
        }
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        var rows = BuildRows(eventData.Context);
        if (rows is not null)
        {
            _writing = true;
            try
            {
                eventData.Context!.Set<EntityChangeLog>().AddRange(rows);
                await eventData.Context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Fail(eventData.Context, rows, ex);
            }
            finally
            {
                _writing = false;
            }
        }
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Reset();

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Reset();
        return Task.CompletedTask;
    }

    // ---- internals -------------------------------------------------------------

    private void Capture(DbContext? context)
    {
        if (_writing || context is null || !_options.Value.Enabled)
        {
            return;
        }

        List<Pending>? list = null;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            var clr = entry.Metadata.ClrType;
            if (!ChangeLogRegistry.IsAudited(clr))
            {
                continue;
            }

            ChangeAction action;
            switch (entry.State)
            {
                case EntityState.Added: action = ChangeAction.Insert; break;
                case EntityState.Modified: action = ChangeAction.Update; break;
                case EntityState.Deleted: action = ChangeAction.Delete; break;
                default: continue;
            }

            var pkNames = entry.Metadata.FindPrimaryKey()?.Properties
                .Select(p => p.Name).ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>();

            var fields = entry.Properties
                .Where(p => !pkNames.Contains(p.Metadata.Name)
                            && !ChangeLogRegistry.ExcludedFieldNames.Contains(p.Metadata.Name)
                            && (action != ChangeAction.Update || p.IsModified))
                .Select(p => p.Metadata.Name)
                .ToList();

            // An Update whose only touched columns are excluded (login bookkeeping, audit trio) is noise.
            if (action == ChangeAction.Update && fields.Count == 0)
            {
                continue;
            }

            var type = ChangeLogRegistry.NormalizeTypeName(clr);
            list ??= new List<Pending>();
            list.Add(new Pending
            {
                Entry = entry,
                EntityType = type,
                Action = action,
                Fields = fields,
                // Insert keys are generated during the save → read them in phase 2. Deleted rows
                // become inaccessible after the save, so capture key + label now.
                EntityId = action == ChangeAction.Insert ? null : PrimaryKeyString(entry),
                EntityLabel = action == ChangeAction.Insert ? null : ChangeLogLabeler.Describe(entry, type),
            });
        }

        if (list is null)
        {
            _pending = null;
            return;
        }

        _occurredAtUtc = DateTime.UtcNow;
        _userId = _currentUser?.UserId ?? 0;
        _correlationId = _correlation?.CurrentId;
        _pending = list;
    }

    private List<EntityChangeLog>? BuildRows(DbContext? context)
    {
        var pending = _pending;
        _pending = null; // clear before the nested save so re-entry is a no-op
        if (pending is null || context is null)
        {
            return null;
        }

        var rows = new List<EntityChangeLog>(pending.Count);
        foreach (var p in pending)
        {
            rows.Add(new EntityChangeLog
            {
                OccurredAtUtc = _occurredAtUtc,
                UserId = _userId,
                EntityType = p.EntityType,
                EntityId = p.EntityId ?? PrimaryKeyString(p.Entry),           // Insert: generated key now known
                EntityLabel = p.EntityLabel ?? ChangeLogLabeler.Describe(p.Entry, p.EntityType),
                Action = p.Action,
                Changes = JsonSerializer.Serialize(p.Fields, JsonOpts),
                CorrelationId = _correlationId,
            });
        }
        return rows;
    }

    private void Fail(DbContext? context, List<EntityChangeLog> rows, Exception ex)
    {
        // Fail-open: a logging failure must never break the business write. Detach the un-saved log
        // rows so they cannot flush on a later save, and warn.
        _logger?.LogWarning(ex, "Change-log capture failed for {Count} row(s); the business write is unaffected.", rows.Count);
        if (context is null)
        {
            return;
        }
        foreach (var row in rows)
        {
            var entry = context.Entry(row);
            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private void Reset()
    {
        _pending = null;
        _writing = false;
    }

    private static string PrimaryKeyString(EntityEntry entry)
    {
        var pk = entry.Metadata.FindPrimaryKey();
        if (pk is null)
        {
            return "?";
        }
        return string.Join("|", pk.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? string.Empty));
    }

    private sealed class Pending
    {
        public required EntityEntry Entry { get; init; }
        public required string EntityType { get; init; }
        public required ChangeAction Action { get; init; }
        public required List<string> Fields { get; init; }
        public string? EntityId { get; set; }
        public string? EntityLabel { get; set; }
    }
}
