using Neurocorp.Api.Core.BusinessObjects.Lookups;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface ISpecialtyTypeService
{
    Task<IEnumerable<SpecialtyTypeInfo>> GetAllAsync();
    Task<SpecialtyTypeInfo?> GetByIdAsync(int id);
    Task<SpecialtyTypeInfo> CreateAsync(SpecialtyTypeCreateRequest request);
    Task<bool> UpdateAsync(int id, SpecialtyTypeUpdateRequest request);
}
