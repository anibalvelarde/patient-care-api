namespace Neurocorp.Api.Core.Entities;

/// <summary>What happened to a row (matches the MySQL enum on EntityChangeLog.Action).</summary>
public enum ChangeAction
{
    Insert,
    Update,
    Delete,
}

/// <summary>
/// WP-54: one append-only row per insert/update/delete made through the app, written by the
/// SaveChanges interceptor. Deliberately NOT <see cref="AuditableEntityBase"/> — the table IS the
/// audit (<see cref="OccurredAtUtc"/> + <see cref="UserId"/> are its created-stamp and author) and
/// a row is never updated. <see cref="Changes"/> holds a JSON array of changed field NAMES only —
/// never values (owner ruling G2), so no PII enters this table.
/// </summary>
public class EntityChangeLog
{
    /// <summary>bigint PK (the first bigint PK in the schema; a high-volume log warrants it).</summary>
    public long Id { get; set; }

    /// <summary>UTC instant of the SaveChanges that wrote the change. Presentation converts to the viewer tz.</summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Acting SystemUser id; 0 = System (no request user). No FK by convention.</summary>
    public int UserId { get; set; }

    /// <summary>Entity name as the API models it (Undefined* subclasses normalised to the base name).</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>PK of the changed row as a string; composite keys joined with '|'.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Best-effort human label captured at write time from the row's own scalars (may be null; never a join).</summary>
    public string? EntityLabel { get; set; }

    public ChangeAction Action { get; set; }

    /// <summary>JSON array of changed field-name strings, e.g. ["Amount","DiscountAmount"]. Never any values.</summary>
    public string Changes { get; set; } = "[]";

    /// <summary>HTTP request id (TraceIdentifier) grouping every change from one request; null outside requests.</summary>
    public string? CorrelationId { get; set; }
}
