using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Payments;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface IPaymentTypeService
{
    Task<IEnumerable<PaymentTypeInfo>> GetAllAsync();
    Task<PaymentTypeInfo?> GetByIdAsync(int id);
    Task<PaymentTypeInfo> CreateAsync(PaymentTypeCreateRequest request);
    Task<bool> UpdateAsync(int id, PaymentTypeUpdateRequest request);
}
