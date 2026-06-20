using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface ISessionServicePaymentRepository : IRepository<SessionServicePayment>
{
    /// <summary>All service-payment allocations touching any of the given sessions.</summary>
    Task<IReadOnlyList<SessionServicePayment>> GetBySessionIdsAsync(IEnumerable<int> sessionIds);
}
