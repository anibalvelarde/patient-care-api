using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class SessionPaymentRepository(ApplicationDbContext dbContext) :
    EfRepository<SessionPayment>(dbContext), ISessionPaymentRepository
{
    public async Task<IReadOnlyList<SessionPayment>> GetByPaymentIdAsync(int paymentId)
    {
        return await _dbContext.SessionPayments
            .Where(sp => sp.PaymentId == paymentId)
            .ToListAsync();
    }

    public async Task DeleteByPaymentIdAsync(int paymentId)
    {
        var items = await _dbContext.SessionPayments
            .Where(sp => sp.PaymentId == paymentId)
            .ToListAsync();
        _dbContext.SessionPayments.RemoveRange(items);
        await _dbContext.SaveChangesAsync();
    }
}
