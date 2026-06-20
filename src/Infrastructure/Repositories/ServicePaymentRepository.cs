using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class ServicePaymentRepository(ApplicationDbContext dbContext) :
    EfRepository<ServicePayment>(dbContext), IServicePaymentRepository
{
    private IQueryable<ServicePayment> WithDetails() =>
        _dbContext.ServicePayments
            .Include(sp => sp.Therapist).ThenInclude(t => t.User)
            .Include(sp => sp.PaymentType)
            .Include(sp => sp.SessionServicePayments).ThenInclude(ssp => ssp.TherapySession).ThenInclude(ts => ts.Patient).ThenInclude(pt => pt!.User);

    public async Task<IReadOnlyList<ServicePayment>> GetByTherapistIdAndDateRangeAsync(int therapistId, DateTime from, DateTime to)
    {
        return await WithDetails()
            .Where(sp => sp.TherapistId == therapistId && sp.PaymentDate >= from && sp.PaymentDate <= to)
            .OrderByDescending(sp => sp.PaymentDate)
            .ToListAsync();
    }

    public async Task<ServicePayment?> GetByIdWithDetailsAsync(int servicePaymentId)
    {
        return await WithDetails()
            .FirstOrDefaultAsync(sp => sp.Id == servicePaymentId);
    }

    public async Task<IReadOnlyList<ServicePayment>> GetByIdsWithDetailsAsync(IEnumerable<int> servicePaymentIds)
    {
        var ids = servicePaymentIds.Distinct().ToList();
        if (ids.Count == 0) return new List<ServicePayment>();

        return await WithDetails()
            .Where(sp => ids.Contains(sp.Id))
            .OrderByDescending(sp => sp.PaymentDate)
            .ToListAsync();
    }
}
