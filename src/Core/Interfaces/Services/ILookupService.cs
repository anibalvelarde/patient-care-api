using Neurocorp.Api.Core.BusinessObjects.Lookups;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface ILookupService
{
    bool IsValidTableName(string tableName);
    Task<IEnumerable<LookupItem>> GetAllAsync(string tableName);
    Task<LookupItem?> GetByIdAsync(string tableName, int id);
    Task<LookupItem> CreateAsync(string tableName, LookupCreateRequest request);
    Task<bool> UpdateAsync(string tableName, int id, LookupUpdateRequest request);
}
