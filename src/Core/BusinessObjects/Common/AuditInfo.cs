using System;
using System.Text.Json.Serialization;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.BusinessObjects.Common;

/// <summary>
/// WP-31 (U1): additive audit block for the row ⓘ popover. <see cref="UpdatedBy"/> is a display
/// string ("Lastname, Firstname" or "System"); the raw updater id is carried internally for
/// batched name resolution and is never serialized. Creator identity is NOT stored in the schema
/// (only <c>LastUpdatedByUserId</c>) — out of scope.
/// </summary>
public class AuditInfo
{
    public const string SystemActor = "System";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = SystemActor;

    /// <summary>Raw updater id, resolved to <see cref="UpdatedBy"/> after materialization. Not serialized.</summary>
    [JsonIgnore]
    public int UpdatedByUserId { get; set; }

    /// <summary>
    /// Patient/Caretaker are aggregates (a domain entity plus its <see cref="User"/> row, both
    /// auditable — e.g. name edits stamp the user, MRN/flag edits stamp the entity). "Last updated"
    /// is the later of the two stamps, attributed to whoever made that later change. "Created" uses
    /// the user row's creation timestamp (set reliably at onboarding; the entity's own
    /// <c>CreatedTimestamp</c> is unset on some legacy imports).
    /// </summary>
    public static AuditInfo FromPersonAggregate(AuditableEntityBase record, User user)
    {
        var userIsLater = user.LastUpdatedTimestamp >= record.LastUpdatedTimestamp;
        return new AuditInfo
        {
            CreatedAt = user.CreatedTimestamp,
            UpdatedAt = userIsLater ? user.LastUpdatedTimestamp : record.LastUpdatedTimestamp,
            UpdatedByUserId = userIsLater ? user.LastUpdatedByUserId : record.LastUpdatedByUserId,
        };
    }

    /// <summary>Audit block straight from a single entity's own audit trio (e.g. TherapySession).</summary>
    public static AuditInfo FromEntity(AuditableEntityBase entity) => new()
    {
        CreatedAt = entity.CreatedTimestamp,
        UpdatedAt = entity.LastUpdatedTimestamp,
        UpdatedByUserId = entity.LastUpdatedByUserId,
    };
}
