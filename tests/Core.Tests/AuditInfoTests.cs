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
}
