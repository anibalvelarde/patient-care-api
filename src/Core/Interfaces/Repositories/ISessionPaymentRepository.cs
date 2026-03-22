using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface ISessionPaymentRepository : IRepository<SessionPayment>
{
    Task<IReadOnlyList<SessionPayment>> GetByPaymentIdAsync(int paymentId);
    Task DeleteByPaymentIdAsync(int paymentId);
}