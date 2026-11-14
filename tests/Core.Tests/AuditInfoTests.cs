using System;
using Xunit;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.Entities;

namespace Core.Tests;

// WP-31 (U1): the audit block's timestamp/updater selection rules.
public class AuditInfoTests
{
    [Fact]
    public void FromEntity_CopiesTheTrio_DefaultsSystem()
    {
        var ts = new TherapySession
        {
            CreatedTimestamp = new DateTime(2025, 1, 1),
            LastUpdatedTimestamp = new DateTime(2025, 6, 1),
            LastUpdatedByUserId = 7,
        };

        var audit = AuditInfo.FromEntity(ts);

        Assert.Equal(new DateTime(2025, 1, 1), audit.CreatedAt);
        Assert.Equal(new DateTime(2025, 6, 1), audit.UpdatedAt);
        Assert.Equal(7, audit.UpdatedByUserId);
        Assert.Equal("System", audit.UpdatedBy); // not resolved yet
    }

    [Fact]
    public void FromPersonAggregate_UserEditedLater_AttributesToUser()
    {
        var patient = new Patient { LastUpdatedTimestamp = new DateTime(2025, 1, 1), LastUpdatedByUserId = 3 };
        var user = new User
        {
            CreatedTimestamp = new DateTime(2024, 1, 1),
            LastUpdatedTimestamp = new DateTime(2025, 6, 1),
            LastUpdatedByUserId = 9,
        };

        var audit = AuditInfo.FromPersonAggregate(patient, user);

        Assert.Equal(new DateTime(2024, 1, 1), audit.CreatedAt);  // user's creation
        Assert.Equal(new DateTime(2025, 6, 1), audit.UpdatedAt);  // user edited more recently
        Assert.Equal(9, audit.UpdatedByUserId);
    }

    [Fact]
    public void FromPersonAggregate_EntityEditedLater_AttributesToEntity()
    {
        var patient = new Patient { LastUpdatedTimestamp = new DateTime(2025, 7, 1), LastUpdatedByUserId = 3 };
        var user = new User
        {
            CreatedTimestamp = new DateTime(2024, 1, 1),
            LastUpdatedTimestamp = new DateTime(2025, 6, 1),
            LastUpdatedByUserId = 9,
        };

        var audit = AuditInfo.FromPersonAggregate(patient, user);

        Assert.Equal(new DateTime(2025, 7, 1), audit.UpdatedAt);  // entity edited more recently
        Assert.Equal(3, audit.UpdatedByUserId);
    }
    // WP-60 follow-up B5: audit stamps are stored as UTC (API writes DateTime.UtcNow; prod MySQL
    // runs on UTC, so DB-default CURRENT_TIMESTAMP is UTC too) but MySQL reads them back as
    // Kind=Unspecified, which serialized without 'Z' → the popover showed them 5 h off.
    [Fact]
    public void FromEntity_PinsUtcKind_SoJsonEndsInZ()
    {
        var ts = new TherapySession
        {
            CreatedTimestamp = DateTime.SpecifyKind(new DateTime(2025, 1, 1, 14, 30, 0), DateTimeKind.Unspecified),
            LastUpdatedTimestamp = DateTime.SpecifyKind(new DateTime(2025, 6, 1, 1, 15, 0), DateTimeKind.Unspecified),
        };

        var audit = AuditInfo.FromEntity(ts);

        Assert.Equal(DateTimeKind.Utc, audit.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, audit.UpdatedAt.Kind);
        var json = System.Text.Json.JsonSerializer.Serialize(audit);
        Assert.Contains("\"CreatedAt\":\"2025-01-01T14:30:00Z\"", json);
        Assert.Contains("\"UpdatedAt\":\"2025-06-01T01:15:00Z\"", json);
    }

    [Fact]
    public void FromPersonAggregate_PinsUtcKind()
    {
        var patient = new Patient { LastUpdatedTimestamp = new DateTime(2025, 7, 1, 23, 0, 0) };
        var user = new User
        {
            CreatedTimestamp = new DateTime(2024, 1, 1, 8, 0, 0),
            LastUpdatedTimestamp = new DateTime(2025, 6, 1),
        };

        var audit = AuditInfo.FromPersonAggregate(patient, user);

        Assert.Equal(DateTimeKind.Utc, audit.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, audit.UpdatedAt.Kind);
        Assert.Equal(new DateTime(2025, 7, 1, 23, 0, 0), audit.UpdatedAt); // value unchanged, only Kind
    }
}
