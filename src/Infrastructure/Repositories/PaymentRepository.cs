using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class PaymentRepository(ApplicationDbContext dbContext) :
    EfRepository<Payment>(dbContext), IPaymentRepository
{
    public async Task<IReadOnlyList<Payment>> GetByCaretakerIdAsync(int caretakerId)
    {
        return await _dbContext.Payments
            .Include(p => p.Caretaker).ThenInclude(c => c.User)
            .Include(p => p.PaymentType)
            .Include(p => p.SessionPayments).ThenInclude(sp => sp.TherapySession).ThenInclude(ts => ts.Patient).ThenInclude(pt => pt.User)
            .Include(p => p.SessionPayments).ThenInclude(sp => sp.TherapySession).ThenInclude(ts => ts.Therapist).ThenInclude(th => th.User)
            .Where(p => p.CaretakerId == caretakerId)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync();
    }

    public async Task<Payment?> GetByIdWithDetailsAsync(int paymentId)
    {
        return await _dbContext.Payments
            .Include(p => p.Caretaker).ThenInclude(c => c.User)
            .Include(p => p.PaymentType)
            .Include(p => p.SessionPayments).ThenInclude(sp => sp.TherapySession).ThenInclude(ts => ts.Patient).ThenInclude(pt => pt.User)
            .Include(p => p.SessionPayments).ThenInclude(sp => sp.TherapySession).ThenInclude(ts => ts.Therapist).ThenInclude(th => th.User)
            .FirstOrDefaultAsync(p => p.Id == paymentId);
    }

    public async Task<IReadOnlyList<Payment>> GetAllWithDetailsAsync()
    {
        return await _dbContext.Payments
            .Include(p => p.Caretaker).ThenInclude(c => c.User)
            .Include(p => p.PaymentType)
            .Include(p => p.SessionPayments).ThenInclude(sp => sp.TherapySession).ThenInclude(ts => ts.Patient).ThenInclude(pt => pt.User)
            .Include(p => p.SessionPayments).ThenInclude(sp => sp.TherapySession).ThenInclude(ts => ts.Therapist).ThenInclude(th => th.User)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync();
    }
}
