using System.Text.Json;
using Neurocorp.Api.Core.BusinessObjects.ChangeLog;
using Neurocorp.Api.Core.Services;
using Xunit;

namespace Neurocorp.Api.Core.Tests;

/// <summary>WP-60: Panama "today", the Sunday schedule rule, and change-log UTC instants.</summary>
public class Wp60DatesTimezonesTests
{
    [Theory]
    [InlineData("2026-11-09T01:30:00Z", "2026-11-08")] // 8:30 PM Nov 8 in Panama — NOT tomorrow
    [InlineData("2026-11-09T04:59:59Z", "2026-11-08")] // 11:59:59 PM Panama
    [InlineData("2026-11-09T05:00:00Z", "2026-11-09")] // midnight Panama
    [InlineData("2026-11-08T12:00:00Z", "2026-11-08")]
    public void ClinicClock_Today_IsThePanamaCalendarDay(string utc, string expected)
    {
        var utcNow = DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
        Assert.Equal(DateOnly.Parse(expected), ClinicClock.Today(utcNow));
    }

    [Theory]
    [InlineData("2026-11-08", "2026-11-09")] // Sunday → the COMING Monday (owner ruling Q-3)
    [InlineData("2026-11-09", "2026-11-09")] // Monday → itself
    [InlineData("2026-11-12", "2026-11-09")] // Thursday → that week's Monday
    [InlineData("2026-11-14", "2026-11-09")] // Saturday → that week's Monday
    public void ScheduleMatrix_WeekStart_SundayShowsTheComingWeek(string date, string monday)
    {
        Assert.Equal(DateOnly.Parse(monday), ScheduleMatrixService.GetMonday(DateOnly.Parse(date)));
    }

    [Fact]
    public void ChangeLog_Instants_AreUtc_AndSerializeWithZ_EvenWhenReadBackUnspecified()
    {
        // What MySQL hands back: the right UTC wall time, but Kind = Unspecified.
        var fromDb = new DateTime(2026, 11, 8, 18, 48, 1, DateTimeKind.Unspecified);

        var entry = new ChangeLogEntry { OccurredAtUtc = fromDb };
        var byUser = new ChangeLogUserCount { LastOccurredAtUtc = fromDb };
        var byType = new ChangeLogEntityTypeCount { LastOccurredAtUtc = fromDb };

        Assert.Equal(DateTimeKind.Utc, entry.OccurredAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, byUser.LastOccurredAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, byType.LastOccurredAtUtc.Kind);
        Assert.Equal(18, entry.OccurredAtUtc.Hour); // the instant itself is not shifted
        Assert.EndsWith("Z\"", JsonSerializer.Serialize(entry.OccurredAtUtc));
        Assert.EndsWith("Z\"", JsonSerializer.Serialize(byUser.LastOccurredAtUtc));
    }
}
