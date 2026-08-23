using System.Collections;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Web.Authorization;
using Xunit;

namespace Neurocorp.Api.Web.Tests.Conventions;

/// <summary>
/// WP-55 B-4: the generic LookupsController route keys (LookupService.LookupTypes) and the per-type
/// authorization map (LookupManageAuthorizeAttribute.ManageClaimByTable) MUST cover the same table
/// names. If the attribute map is missing a key the LookupService accepts, that table's manage
/// endpoint silently falls through the filter UNGATED (the filter returns early for unknown tables) —
/// a typo becomes a hole. This keeps the two hand-maintained lists in lockstep.
/// </summary>
public class LookupKeysConformanceTests
{
    private static HashSet<string> PrivateStaticDictKeys(System.Type type, string field)
    {
        var value = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        return ((IDictionary)value!).Keys.Cast<string>().ToHashSet(System.StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void LookupManageClaimMap_Covers_ExactlyTheLookupServiceTables()
    {
        var routeKeys = PrivateStaticDictKeys(typeof(LookupService), "LookupTypes");
        var claimKeys = PrivateStaticDictKeys(typeof(LookupManageAuthorizeAttribute), "ManageClaimByTable");

        claimKeys.Should().BeEquivalentTo(routeKeys,
            "every table LookupService recognizes must have a manage-claim mapping in " +
            "LookupManageAuthorizeAttribute (and vice versa) — a missing key means that lookup's " +
            "Create/Update endpoint is authorized by nothing.");
    }
}
