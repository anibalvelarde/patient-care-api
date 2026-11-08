using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Neurocorp.Api.Web.Tests.Conventions;

/// <summary>
/// WP-60 (F-19): "today" is the CLINIC's (Panama) calendar day, never the UTC date. The deployed
/// container runs UTC, so from 19:00 Panama time a UTC "today" is already tomorrow — statements,
/// payroll payment dates and the schedule all came out a day late in the evening. Every "today"
/// must come from <c>ClinicClock.Today()</c>; this scan stops new UTC-today code from creeping back.
/// </summary>
public class ClinicTodayGuardTests
{
    private static readonly Regex UtcToday = new(
        @"DateOnly\.FromDateTime\(\s*DateTime\.UtcNow|DateTime\.UtcNow\.Date\b|DateTime\.UtcNow:yyyy-MM-dd",
        RegexOptions.Compiled);

    // Deliberate exceptions, each with its reason.
    private static readonly Dictionary<string, string> Allowed = new()
    {
        // The ONE source of the clinic day.
        ["Core/Services/ClinicClock.cs"] = "definition",
        // 35-day delinquency cutoff: must stay in step with TherapySession.IsPastDeadline(), which is
        // UTC arithmetic by design (ClinicClock doc: the two clocks are independent by owner ruling).
        ["Infrastructure/Repositories/SessionEventRepository.cs"] = "IsPastDeadline mirror",
    };

    [Fact]
    public void NoUtcToday_OutsideClinicClock()
    {
        var srcDir = FindSrcDir();
        srcDir.Should().NotBeNull();

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcDir!, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;
            var rel = Path.GetRelativePath(srcDir!, file).Replace('\\', '/');
            if (Allowed.ContainsKey(rel)) continue;

            var lines = StripComments(File.ReadAllText(file)).Split('\n');
            for (var i = 0; i < lines.Length; i++)
                if (UtcToday.IsMatch(lines[i]))
                    violations.Add($"{rel}:{i + 1}: {lines[i].Trim()}");
        }

        violations.Should().BeEmpty("\"today\" must be ClinicClock.Today() (Panama), not the UTC date");
    }

    private static string StripComments(string code)
    {
        code = Regex.Replace(code, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return string.Join('\n', code.Split('\n').Select(l =>
        {
            var idx = l.IndexOf("//", System.StringComparison.Ordinal);
            return idx >= 0 ? l[..idx] : l;
        }));
    }

    private static string? FindSrcDir()
    {
        for (var dir = new DirectoryInfo(System.AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, "Core")) && Directory.Exists(Path.Combine(candidate, "Web")))
                return candidate;
        }
        return null;
    }
}
