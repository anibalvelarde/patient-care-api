using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Xunit;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Services;

namespace Core.Tests;

// WP-31 (U1): name enrichment fills UpdatedBy from a single batched resolver call and defaults to
// "System" for id 0 / unresolved ids / absent audit.
public class AuditEnrichmentTests
{
    private sealed class Row : IHasAudit
    {
        public AuditInfo? Audit { get; set; }
    }

    [Fact]
    public async Task ResolveNamesAsync_FillsResolvedNames_And_DefaultsSystem()
    {
        var items = new List<IHasAudit>
        {
            new Row { Audit = new AuditInfo { UpdatedByUserId = 5 } },
            new Row { Audit = new AuditInfo { UpdatedByUserId = 0 } },   // system actor
            new Row { Audit = new AuditInfo { UpdatedByUserId = 99 } },  // not in the dictionary
            new Row { Audit = null },                                    // no block — skipped
        };
        var resolver = new Mock<IUserNameResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, string> { [5] = "Doe, John" });

        await AuditEnrichment.ResolveNamesAsync(items, resolver.Object);

        Assert.Equal("Doe, John", ((Row)items[0]).Audit!.UpdatedBy);
        Assert.Equal("System", ((Row)items[1]).Audit!.UpdatedBy);
        Assert.Equal("System", ((Row)items[2]).Audit!.UpdatedBy);
        Assert.Null(((Row)items[3]).Audit);
    }

    [Fact]
    public async Task ResolveNamesAsync_CallsResolverOnce_ForTheWholeBatch()
    {
        var resolver = new Mock<IUserNameResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, string>());
        var items = new List<IHasAudit>
        {
            new Row { Audit = new AuditInfo { UpdatedByUserId = 5 } },
            new Row { Audit = new AuditInfo { UpdatedByUserId = 5 } },
            new Row { Audit = new AuditInfo { UpdatedByUserId = 6 } },
        };

        await AuditEnrichment.ResolveNamesAsync(items, resolver.Object);

        resolver.Verify(r => r.ResolveAsync(It.IsAny<IEnumerable<int>>()), Times.Once); // no N+1
    }

    [Fact]
    public async Task ResolveNamesAsync_SkipsResolver_WhenNothingCarriesAudit()
    {
        var resolver = new Mock<IUserNameResolver>(MockBehavior.Strict);

        await AuditEnrichment.ResolveNamesAsync(new List<IHasAudit> { new Row { Audit = null } }, resolver.Object);

        resolver.Verify(r => r.ResolveAsync(It.IsAny<IEnumerable<int>>()), Times.Never);
    }
}
