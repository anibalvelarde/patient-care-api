using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class SessionServicePaymentRepository(ApplicationDbContext dbContext) :
    EfRepository<SessionServicePayment>(dbContext), ISessionServicePaymentRepository
{
    public async Task<IReadOnlyList<SessionServicePayment>> GetBySessionIdsAsync(IEnumerable<int> sessionIds)
    {
        var ids = sessionIds.Distinct().ToList();
        if (ids.Count == 0) return new List<SessionServicePayment>();

        return await _dbContext.SessionServicePayments
            .Where(ssp => ids.Contains(ssp.TherapySessionId))
            .ToListAsync();
    }
}
