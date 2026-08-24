using System.Globalization;

namespace Neurocorp.Api.Core.BusinessObjects.ChangeLog;

/// <summary>
/// WP-55 (B-1): the ONE home for the change-log summary period grain. Before this, the five period
/// names were a raw string set in <c>AdminChangeLogController</c> plus four parallel
/// <c>switch</c> expressions in <c>ChangeLogService</c> (bucket-start / advance / key / label) — five
/// places to touch, in lockstep, to add or rename a grain. Now the names live here and each grain's
/// four behaviours sit together, so a new period is one edit.
/// </summary>
public static class ChangeLogPeriods
{
    public const string Day = "day";
    public const string Week = "week";
    public const string Month = "month";
    public const string Quarter = "quarter";
    public const string Year = "year";

    /// <summary>Accepted grains (case-insensitive), for query validation.</summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Day, Week, Month, Quarter, Year };

    /// <summary>Start of the bucket <paramref name="day"/> falls in (weeks are ISO — Monday).</summary>
    public static DateTime Start(DateTime day, string period) => period switch
    {
        Week => day.AddDays(-((int)day.DayOfWeek == 0 ? 6 : (int)day.DayOfWeek - 1)), // ISO week: Monday
        Month => new DateTime(day.Year, day.Month, 1),
        Quarter => new DateTime(day.Year, ((day.Month - 1) / 3) * 3 + 1, 1),
        Year => new DateTime(day.Year, 1, 1),
        _ => day.Date, // day
    };

    /// <summary>Start of the next bucket after <paramref name="start"/>.</summary>
    public static DateTime Next(DateTime start, string period) => period switch
    {
        Week => start.AddDays(7),
        Month => start.AddMonths(1),
        Quarter => start.AddMonths(3),
        Year => start.AddYears(1),
        _ => start.AddDays(1),
    };

    /// <summary>Stable machine key for the bucket <paramref name="day"/> falls in.</summary>
    public static string Key(DateTime day, string period) => period switch
    {
        Week => $"W{ISOWeek.GetYear(day):0000}-{ISOWeek.GetWeekOfYear(day):00}",
        Month => $"{day.Year:0000}-{day.Month:00}",
        Quarter => $"{day.Year:0000}-Q{(day.Month - 1) / 3 + 1}",
        Year => $"{day.Year:0000}",
        _ => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    };

    /// <summary>Human label for the bucket starting at <paramref name="start"/>.</summary>
    public static string Label(DateTime start, string period) => period switch
    {
        Week => $"{ISOWeek.GetYear(start):0000}-W{ISOWeek.GetWeekOfYear(start):00}",
        Month => $"{start.Year:0000}-{start.Month:00}",
        Quarter => $"{start.Year:0000}-Q{(start.Month - 1) / 3 + 1}",
        Year => $"{start.Year:0000}",
        _ => start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    };
}
