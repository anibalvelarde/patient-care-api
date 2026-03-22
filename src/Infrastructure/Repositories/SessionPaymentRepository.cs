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

    public async Task<IReadOnlyList<SessionPayment>> GetBySessionIdWithDetailsAsync(int sessionId)
    {
        return await _dbContext.SessionPayments
            .Include(sp => sp.Payment).ThenInclude(p => p.Caretaker).ThenInclude(c => c.User)
            .Include(sp => sp.Payment).ThenInclude(p => p.PaymentType)
            .Where(sp => sp.TherapySessionId == sessionId)
            .OrderByDescending(sp => sp.Payment.PaymentDate)
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
