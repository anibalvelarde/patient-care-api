using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface ISessionPaymentRepository : IRepository<SessionPayment>
{
    Task<IReadOnlyList<SessionPayment>> GetByPaymentIdAsync(int paymentId);
    Task DeleteByPaymentIdAsync(int paymentId);
    Task<IReadOnlyList<SessionPayment>> GetBySessionIdWithDetailsAsync(int sessionId);
    /// <summary>WP-58: SUM(AmountAllocated) for one session (0 when none) — the ledger half of AmountPaid.</summary>
    Task<decimal> GetAllocatedTotalForSessionAsync(int sessionId);
}