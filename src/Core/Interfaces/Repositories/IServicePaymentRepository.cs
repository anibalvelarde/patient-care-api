using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface IServicePaymentRepository : IRepository<ServicePayment>
{
    Task<IReadOnlyList<ServicePayment>> GetByTherapistIdAndDateRangeAsync(int therapistId, DateTime from, DateTime to);
    Task<ServicePayment?> GetByIdWithDetailsAsync(int servicePaymentId);
    Task<IReadOnlyList<ServicePayment>> GetByIdsWithDetailsAsync(IEnumerable<int> servicePaymentIds);

    /// <summary>True if a reversal entry already exists for <paramref name="servicePaymentId"/> (WP-14.5).</summary>
    Task<bool> IsReversedAsync(int servicePaymentId);

    /// <summary>
    /// Of the given original payment ids, returns those that have a reversal entry — one query for the
    /// list view, robust to the reversal falling outside the queried date range.
    /// </summary>
    Task<IReadOnlyCollection<int>> GetReversedOriginalIdsAsync(IEnumerable<int> originalIds);
}
