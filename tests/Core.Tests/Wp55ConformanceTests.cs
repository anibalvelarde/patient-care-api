using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Services;
using Xunit;

namespace Core.Tests;

/// <summary>
/// WP-55 B-4 guards — keep the centralized constants from silently diverging again.
///  - Guard 1: the specialty-price duration validation and SpecialtyPricing.AllowedDurations are
///    ONE source (a second list can't drift back in — this is what B-2a's 40-min bug was).
///  - Guard 2: the C# enum-value constants (relationship / gender / confirmation-result) equal the
///    MySQL enum declarations in patient-care-db/schema/tables/*.sql INCLUDING ORDER — MySQL stores
///    enums by ordinal, so an out-of-order or dropped value is the V036 "quietly wrong" failure mode.
///    Skips when the sibling DB repo isn't checked out (CI wiring is a later side-quest); on a dev
///    box or once CROSS_REPO_READ_TOKEN lands, it runs.
/// </summary>
public class Wp55ConformanceTests
{
    // ── Guard 1: duration validation single-source ──────────────────────────────
    [Fact]
    public void SpecialtyPriceRow_AllowedDurations_AreExactly_SpecialtyPricingAllowedDurations()
    {
        var attr = typeof(SpecialtyPriceAppendRow)
            .GetProperty(nameof(SpecialtyPriceAppendRow.DurationMinutes))!
            .GetCustomAttributes(typeof(AllowedValuesAttribute), false)
            .Cast<AllowedValuesAttribute>()
            .Single();

        var attrValues = attr.Values.Cast<int>().ToArray();

        attrValues.Should().Equal(SpecialtyPricing.AllowedDurations,
            "the [AllowedValues] on the price-append row must match SpecialtyPricing.AllowedDurations " +
            "exactly — there must be exactly one duration source (WP-55 B-2a/B-4); if this fails, " +
            "point one at the other rather than maintaining two lists.");
    }

    // ── Guard 2: C# enum constants ≡ DB enum declarations (order-sensitive) ──────
    [Theory]
    [InlineData("PatientCaretaker.sql", "RelationshipToPatient")]
    [InlineData("Patient.sql", "Gender")]
    [InlineData("AppointmentConfirmation.sql", "ConfirmationResult")]
    public void CSharpEnumConstants_MatchDbEnum_IncludingOrder(string schemaFile, string column)
    {
        var tablesDir = FindSiblingSchemaTablesDir();
        if (tablesDir is null) return; // sibling DB repo not checked out — skip (see class summary)

        var path = Path.Combine(tablesDir, schemaFile);
        File.Exists(path).Should().BeTrue($"expected schema snapshot {schemaFile} under {tablesDir}");

        var dbValues = ParseEnumValues(File.ReadAllText(path), column);
        var csharpValues = column switch
        {
            "RelationshipToPatient" => CaretakerRelationships.All,
            "Gender" => Genders.All,
            "ConfirmationResult" => ConfirmationValues.AllResults,
            _ => throw new System.ArgumentOutOfRangeException(nameof(column)),
        };

        csharpValues.Should().Equal(dbValues,
            $"the C# constants for {column} must equal the MySQL enum in {schemaFile} including order " +
            "(enums are stored by ordinal — append-only). Refresh the constants or the schema snapshot " +
            "(WP-55A) so they agree.");
    }

    private static string[] ParseEnumValues(string ddl, string column)
    {
        // Match:  `Column` enum('a','b',...)   (backticks optional, case-insensitive column match)
        var m = Regex.Match(ddl,
            $@"`?{Regex.Escape(column)}`?\s+enum\((?<vals>[^)]*)\)",
            RegexOptions.IgnoreCase);
        m.Success.Should().BeTrue($"could not find an enum(...) declaration for column {column}");

        return Regex.Matches(m.Groups["vals"].Value, @"'((?:[^']|'')*)'")
            .Select(v => v.Groups[1].Value.Replace("''", "'"))
            .ToArray();
    }

    private static string? FindSiblingSchemaTablesDir()
    {
        for (var dir = new DirectoryInfo(System.AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "patient-care-db", "schema", "tables");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}
