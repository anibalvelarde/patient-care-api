using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Services;

/// <summary>
/// WP-31 (U1): resolves audit updater ids to "Lastname, Firstname" via a single batched query over
/// SystemUsers. <c>LastUpdatedByUserId</c> is a plain int (not an FK), so unmatched ids simply drop
/// out — callers render "System" for them.
/// </summary>
public class UserNameResolver(ApplicationDbContext dbContext) : IUserNameResolver
{
    public async Task<IReadOnlyDictionary<int, string>> ResolveAsync(IEnumerable<int> userIds)
    {
        // Id 0 = DEFAULT_SYSTEM_USER_ID (legacy imports/scripts) — never a real user.
        var ids = userIds.Where(id => id != 0).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, string>();

        var rows = await dbContext.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.MiddleName })
            .ToListAsync();

        return rows.ToDictionary(
            r => r.Id,
            r => $"{r.LastName}, {r.FirstName} {r.MiddleName}".Trim());
    }
}
