using Neurocorp.Api.Core.BusinessObjects.Lookups;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface IRoleTypeService
{
    Task<IEnumerable<RoleTypeInfo>> GetAllAsync();
    Task<RoleTypeInfo?> GetByIdAsync(int id);
    Task<RoleTypeInfo> CreateAsync(RoleTypeCreateRequest request);
    Task<bool> UpdateAsync(int id, RoleTypeUpdateRequest request);
}
