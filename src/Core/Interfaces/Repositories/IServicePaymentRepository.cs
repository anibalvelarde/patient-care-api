using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

public interface IServicePaymentRepository : IRepository<ServicePayment>
{
    Task<IReadOnlyList<ServicePayment>> GetByTherapistIdAndDateRangeAsync(int therapistId, DateTime from, DateTime to);
    Task<ServicePayment?> GetByIdWithDetailsAsync(int servicePaymentId);
    Task<IReadOnlyList<ServicePayment>> GetByIdsWithDetailsAsync(IEnumerable<int> servicePaymentIds);
}
