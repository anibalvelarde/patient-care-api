using FluentAssertions;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.ChangeLog;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Services;
using Xunit;

namespace Neurocorp.Api.Core.Tests;

/// <summary>
/// WP-54B read-side service: the day → period rollup (pure C#), list mapping (field-name JSON
/// deserialization + batched name resolution), and purge delegation. Repository + name resolver
/// are mocked.
/// </summary>
public class ChangeLogServiceTests
{
    private readonly Mock<IChangeLogRepository> _repo = new();
    private readonly Mock<IUserNameResolver> _names = new();

    private ChangeLogService NewService() => new(_repo.Object, _names.Object);

    private void SetupSummaryBoilerplate(IReadOnlyList<ChangeLogDayCount> days, IReadOnlyList<ChangeLogUserCount>? byUser = null)
    {
        _repo.Setup(r => r.TotalsAsync(It.IsAny<ChangeLogQuery>())).ReturnsAsync(new ChangeLogTotals());
        _repo.Setup(r => r.ByUserAsync(It.IsAny<ChangeLogQuery>(), It.IsAny<int>()))
            .ReturnsAsync(byUser ?? []);
        _repo.Setup(r => r.ByEntityTypeAsync(It.IsAny<ChangeLogQuery>())).ReturnsAsync([]);
        _repo.Setup(r => r.ByDayAsync(It.IsAny<ChangeLogQuery>())).ReturnsAsync(days);
        _names.Setup(n => n.ResolveAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, string>());
    }

    [Fact]
    public async Task Summary_rolls_days_into_months_zero_filling_the_gap()
    {
        SetupSummaryBoilerplate(
        [
            new ChangeLogDayCount { LocalDay = new DateTime(2026, 6, 20), Inserts = 2 },
            new ChangeLogDayCount { LocalDay = new DateTime(2026, 8, 5), Updates = 3 },
        ]);

        var summary = await NewService().GetSummaryAsync(new ChangeLogQuery
        {
            FromUtc = new DateTime(2026, 6, 15),
            ToUtc = new DateTime(2026, 8, 20),
            TzOffsetMinutes = 0,
            GroupBy = "month",
        });

        summary.ByPeriodOmitted.Should().BeFalse();
        summary.ByPeriod.Select(p => p.Label).Should().Equal("2026-06", "2026-07", "2026-08");
        summary.ByPeriod[0].Inserts.Should().Be(2);
        summary.ByPeriod[0].Total.Should().Be(2);
        summary.ByPeriod[1].Total.Should().Be(0);   // zero-filled July
        summary.ByPeriod[2].Updates.Should().Be(3);
    }

    [Fact]
    public async Task Summary_day_grouping_zero_fills_every_day_in_the_window()
    {
        SetupSummaryBoilerplate(
        [
            new ChangeLogDayCount { LocalDay = new DateTime(2026, 8, 2), Deletes = 1 },
        ]);

        var summary = await NewService().GetSummaryAsync(new ChangeLogQuery
        {
            FromUtc = new DateTime(2026, 8, 1),
            ToUtc = new DateTime(2026, 8, 3, 12, 0, 0),
            GroupBy = "day",
        });

        summary.ByPeriod.Select(p => p.Label).Should().Equal("2026-08-01", "2026-08-02", "2026-08-03");
        summary.ByPeriod[1].Deletes.Should().Be(1);
    }

    [Fact]
    public async Task Summary_omits_the_trend_when_there_are_too_many_buckets()
    {
        SetupSummaryBoilerplate([]);

        var summary = await NewService().GetSummaryAsync(new ChangeLogQuery
        {
            FromUtc = new DateTime(2025, 1, 1),
            ToUtc = new DateTime(2026, 12, 31),   // ~2 years of daily buckets > 372
            GroupBy = "day",
        });

        summary.ByPeriodOmitted.Should().BeTrue();
        summary.ByPeriod.Should().BeEmpty();
    }

    [Fact]
    public async Task Summary_resolves_by_user_display_names()
    {
        SetupSummaryBoilerplate([], [new ChangeLogUserCount { UserId = 7, Total = 5 }]);
        _names.Setup(n => n.ResolveAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, string> { [7] = "Reyes, Ana" });

        var summary = await NewService().GetSummaryAsync(new ChangeLogQuery
        {
            FromUtc = new DateTime(2026, 8, 1),
            ToUtc = new DateTime(2026, 8, 2),
            GroupBy = "day",
        });

        summary.ByUser.Single().DisplayName.Should().Be("Reyes, Ana");
    }

    [Fact]
    public async Task List_maps_entities_deserializes_field_names_and_resolves_the_user()
    {
        _repo.Setup(r => r.ListAsync(It.IsAny<ChangeLogQuery>())).ReturnsAsync(new PagedResult<EntityChangeLog>
        {
            Items =
            [
                new EntityChangeLog
                {
                    Id = 10, OccurredAtUtc = new DateTime(2026, 8, 20), UserId = 7,
                    EntityType = "TherapySession", EntityId = "10933", EntityLabel = "#10933 · 2026-08-20",
                    Action = ChangeAction.Update, Changes = "[\"Amount\",\"DiscountAmount\"]", CorrelationId = "req-1",
                },
                new EntityChangeLog
                {
                    Id = 9, OccurredAtUtc = new DateTime(2026, 8, 20), UserId = 0,
                    EntityType = "Patient", EntityId = "5", Action = ChangeAction.Insert, Changes = "[]",
                },
            ],
            Page = 1, PageSize = 25, TotalCount = 2,
        });
        _names.Setup(n => n.ResolveAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, string> { [7] = "Solis, Maria" });

        var result = await NewService().ListAsync(new ChangeLogQuery());

        result.TotalCount.Should().Be(2);
        var first = result.Items[0];
        first.Action.Should().Be("Update");
        first.ChangedFields.Should().BeEquivalentTo(["Amount", "DiscountAmount"]);
        first.User.DisplayName.Should().Be("Solis, Maria");
        result.Items[1].User.DisplayName.Should().Be("System");     // id 0 → System
        result.Items[1].ChangedFields.Should().BeEmpty();
    }

    [Fact]
    public async Task Purge_delegates_to_the_repository()
    {
        _repo.Setup(r => r.PurgeAsync(1100, false, It.IsAny<DateTime>()))
            .ReturnsAsync(new ChangeLogPurgeResult { OlderThanDays = 1100, Matched = 42, Deleted = 0, DryRun = true });

        var result = await NewService().PurgeAsync(1100, confirm: false);

        result.DryRun.Should().BeTrue();
        result.Matched.Should().Be(42);
        result.Deleted.Should().Be(0);
    }
}
