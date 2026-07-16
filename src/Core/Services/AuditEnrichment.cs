using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

/// <summary>
/// WP-31 (U1): fills <see cref="AuditInfo.UpdatedBy"/> on a batch of DTOs after materialization —
/// one resolver call for the whole page, then a display-name (or "System") per row.
/// </summary>
public static class AuditEnrichment
{
    public static async Task ResolveNamesAsync(IEnumerable<IHasAudit> items, IUserNameResolver resolver)
    {
        var audits = items.Where(i => i.Audit != null).Select(i => i.Audit!).ToList();
        if (audits.Count == 0) return;

        var names = await resolver.ResolveAsync(audits.Select(a => a.UpdatedByUserId));
        foreach (var audit in audits)
        {
            audit.UpdatedBy = names.TryGetValue(audit.UpdatedByUserId, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : AuditInfo.SystemActor;
        }
    }

    /// <summary>Convenience overload for a single DTO (detail endpoints).</summary>
    public static Task ResolveNamesAsync(IHasAudit item, IUserNameResolver resolver) =>
        ResolveNamesAsync(new[] { item }, resolver);
}
