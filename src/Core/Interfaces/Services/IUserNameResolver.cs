using System.Collections.Generic;
using System.Threading.Tasks;

namespace Neurocorp.Api.Core.Interfaces.Services;

/// <summary>
/// WP-31 (U1): batched id → display-name lookup for audit attribution. One query regardless of the
/// id count (never per-row — WP-29's N+1 lesson). Ids of 0 or with no matching user are omitted
/// from the result; callers fall back to "System".
/// </summary>
public interface IUserNameResolver
{
    Task<IReadOnlyDictionary<int, string>> ResolveAsync(IEnumerable<int> userIds);
}
