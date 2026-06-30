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
            // PaymentDate is a date-only business date, so ties (e.g. a reversal + its re-issuance on the
            // same day) are broken by issuance order (auto-increment Id) — newest action first.
            .OrderByDescending(sp => sp.PaymentDate)
            .ThenByDescending(sp => sp.Id)
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
            .ThenByDescending(sp => sp.Id)
            .ToListAsync();
    }

    public async Task<bool> IsReversedAsync(int servicePaymentId)
    {
        return await _dbContext.ServicePayments
            .AnyAsync(sp => sp.ReversesServicePaymentId == servicePaymentId);
    }

    public async Task<IReadOnlyCollection<int>> GetReversedOriginalIdsAsync(IEnumerable<int> originalIds)
    {
        var ids = originalIds.Distinct().ToList();
        if (ids.Count == 0) return Array.Empty<int>();

        return await _dbContext.ServicePayments
            .Where(sp => sp.ReversesServicePaymentId != null && ids.Contains(sp.ReversesServicePaymentId.Value))
            .Select(sp => sp.ReversesServicePaymentId!.Value)
            .Distinct()
            .ToListAsync();
    }
}
