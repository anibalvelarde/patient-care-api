using System.Globalization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Neurocorp.Api.Core.BusinessObjects.ChangeLog;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Infrastructure.Data.ChangeLog;

/// <summary>
/// WP-54 capture rules (D4/D5/D6), shared by the interceptor (what to capture) and the repository
/// (what to list). Capture-everything-minus-exclusions: every mapped entity is audited except the
/// log itself; a curated field-name exclusion set keeps the audit trio and login bookkeeping out of
/// the change list so they never generate noise.
/// </summary>
public static class ChangeLogRegistry
{
    /// <summary>
    /// Field names never listed in <c>Changes</c> (D4). The audit trio (never a business change),
    /// LookupEntityBase's own timestamps (same names), and login bookkeeping that changes on every
    /// sign-in — an Update whose changes are all in this set is not logged at all.
    /// </summary>
    public static readonly IReadOnlySet<string> ExcludedFieldNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "CreatedTimestamp",
        "LastUpdatedTimestamp",
        "LastUpdatedByUserId",
        "LastLoginAt",
        "FailedLoginAttempts",
        "LockoutUntil",
    };

    /// <summary>Every mapped entity is audited except the log table itself.</summary>
    public static bool IsAudited(Type clrType) => clrType != typeof(EntityChangeLog);

    /// <summary>CLR type name with an <c>Undefined</c> null-object prefix normalised to the base entity name.</summary>
    public static string NormalizeTypeName(Type clrType)
    {
        var name = clrType.Name;
        const string undefined = "Undefined";
        return name.StartsWith(undefined, StringComparison.Ordinal) && name.Length > undefined.Length
            ? name[undefined.Length..]
            : name;
    }

    /// <summary>The audited entity types in the model (normalised, distinct, sorted) — stable filter chips.</summary>
    public static IReadOnlyList<ChangeLogEntityTypeInfo> EntityTypes(IModel model)
        => model.GetEntityTypes()
            .Where(t => IsAudited(t.ClrType))
            .Select(t => NormalizeTypeName(t.ClrType))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => new ChangeLogEntityTypeInfo { EntityType = n, DisplayName = n })
            .ToList();
}

/// <summary>
/// WP-54 D6: best-effort human label captured at write time from the row's OWN scalar columns —
/// never a join (the WP-29 N+1 lesson). Survives later deletion. A person's name/MRN may appear
/// here as a row identifier, not a changed value.
/// </summary>
public static class ChangeLogLabeler
{
    public static string? Describe(EntityEntry entry, string normalizedType)
    {
        var id = PrimaryKeyString(entry);
        switch (normalizedType)
        {
            case "User":
            {
                var last = Str(entry, "LastName");
                var first = Str(entry, "FirstName");
                if (last is not null || first is not null)
                {
                    return $"{last ?? "?"}, {first ?? "?"}";
                }
                return $"#{id}";
            }
            case "Patient":
            {
                var mrn = Str(entry, "MedicalRecordNumber");
                return string.IsNullOrWhiteSpace(mrn) ? $"#{id}" : mrn;
            }
            case "TherapySession":
            {
                var d = Get(entry, "SessionDate");
                return d is DateTime dt ? $"#{id} · {dt:yyyy-MM-dd}" : $"#{id}";
            }
            case "Payment":
            {
                var amt = Get(entry, "Amount");
                return amt is decimal m ? $"#{id} · ${m.ToString("0.00", CultureInfo.InvariantCulture)}" : $"#{id}";
            }
            default:
                return $"#{id}";
        }
    }

    private static string PrimaryKeyString(EntityEntry entry)
    {
        var pk = entry.Metadata.FindPrimaryKey();
        if (pk is null)
        {
            return "?";
        }
        var parts = pk.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? string.Empty);
        return string.Join("|", parts);
    }

    private static object? Get(EntityEntry entry, string name)
        => entry.Metadata.FindProperty(name) is null ? null : entry.Property(name).CurrentValue;

    private static string? Str(EntityEntry entry, string name)
    {
        var v = Get(entry, name)?.ToString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
}
