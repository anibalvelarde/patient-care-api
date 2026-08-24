using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Neurocorp.Api.Web.Tests.Conventions;

/// <summary>
/// WP-55 B-4 ratchet: scans src/**/*.cs for the magic-value patterns WP-55 centralized, and holds
/// the count at (or below) a checked-in baseline that can only SHRINK. A new bare status/role/
/// relationship/plan-status literal fails the build until it is routed through the constant home
/// (SessionStatus / RoleTaxonomy / CaretakerRelationships / TreatmentPlan.PlanStatuses /
/// SessionStatus.ConfirmedStatuses). Mechanics mirror the access-control conformance-baseline.txt
/// ratchet: an .actual.txt is written beside the baseline so updating it is a copy, and the compare
/// runs both directions (new violations fail; disappeared baseline entries must be removed).
///
/// Comments and doc-comments are stripped before matching (so a literal named in prose doesn't
/// count), and the const-definition line of each value is exempt (that IS the one source).
/// </summary>
public class MagicValueRatchetTests
{
    private static readonly string BaseDir = System.AppContext.BaseDirectory;
    private static readonly string BaselinePath = Path.Combine(BaseDir, "conventions-baseline.txt");

    private static readonly (string Name, Regex Pattern)[] Banned =
    [
        ("appointment-status-literal", new Regex(@"AppointmentStatusId\s*[=!]=\s*\d", RegexOptions.Compiled)),
        ("role-id-literal", new Regex(@"\bRoleId\s*=\s*\d", RegexOptions.Compiled)),
        ("plan-status-literal", new Regex(@"PlanStatus\s*[=!]=\s*""", RegexOptions.Compiled)),
        ("confirmed-statuses-redeclare", new Regex(@"ConfirmedStatuses\s*=\s*(\[|new)", RegexOptions.Compiled)),
        ("active-statuses-redeclare", new Regex(@"ActiveStatuses\s*=\s*(\[|new)", RegexOptions.Compiled)),
        ("self-relationship-literal", new Regex(@"""Self""", RegexOptions.Compiled)),
    ];

    // Constant-home files legitimately DEFINE these sets as `static readonly` (not `const`, so the
    // per-line `const ` exemption doesn't cover them). Skip them by path so the ratchet can't flag
    // its own source of truth if a definition is ever reformatted onto one line. Const-STRING homes
    // (CaretakerRelationships, TreatmentPlan.PlanStatuses, …) need no entry — their `const ` lines
    // are already exempt.
    private static readonly string[] ConstantHomes = { "SessionStatus.cs" };

    [Fact]
    public void MagicValueLiterals_DoNotExceedTheBaseline()
    {
        var srcDir = FindSrcDir();
        srcDir.Should().NotBeNull("the src/ tree must be locatable from the test output dir");

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcDir!, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;
            if (ConstantHomes.Any(h => file.EndsWith(h, System.StringComparison.OrdinalIgnoreCase)))
                continue; // the definition IS the one source (L-1)

            var rel = Path.GetRelativePath(srcDir!, file).Replace('\\', '/');
            var lines = StripComments(File.ReadAllText(file)).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("const ")) continue; // the definition IS the one source — exempt
                foreach (var (name, pattern) in Banned)
                    if (pattern.IsMatch(line))
                        violations.Add($"{rel}:{i + 1}: {name}");
            }
        }
        violations.Sort(System.StringComparer.Ordinal);

        File.WriteAllLines(Path.Combine(BaseDir, "conventions-baseline.actual.txt"), violations);

        File.Exists(BaselinePath).Should().BeTrue(
            $"conventions-baseline.txt is missing; seed it from conventions-baseline.actual.txt in {BaseDir}");

        var baseline = File.ReadAllLines(BaselinePath)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .OrderBy(l => l, System.StringComparer.Ordinal)
            .ToList();

        var newViolations = violations.Except(baseline).ToList();
        var disappeared = baseline.Except(violations).ToList();

        newViolations.Should().BeEmpty(
            "these magic-value literals are new — route them through the WP-55 constant home instead " +
            "of a bare literal (or, if genuinely unavoidable, add them to conventions-baseline.txt)");
        disappeared.Should().BeEmpty(
            "these baseline entries no longer exist (cleaned up — good!) — remove them from " +
            "conventions-baseline.txt so the ratchet only moves down");
    }

    private static string StripComments(string code)
    {
        code = Regex.Replace(code, @"/\*.*?\*/", "", RegexOptions.Singleline); // block + doc /* */
        return string.Join('\n', code.Split('\n').Select(l =>
        {
            var idx = l.IndexOf("//", System.StringComparison.Ordinal);
            return idx >= 0 ? l[..idx] : l; // line + /// doc comments (crude; may over-trim, never under-flags)
        }));
    }

    private static string? FindSrcDir()
    {
        for (var dir = new DirectoryInfo(BaseDir); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, "Core")) &&
                Directory.Exists(Path.Combine(candidate, "Web")))
                return candidate;
        }
        return null;
    }
}
