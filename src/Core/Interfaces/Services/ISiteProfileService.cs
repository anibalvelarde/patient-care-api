using Neurocorp.Api.Core.BusinessObjects.Sites;

namespace Neurocorp.Api.Core.Interfaces.Services;

public interface ISiteProfileService
{
    Task<IEnumerable<SiteProfile>> GetAllAsync();
    Task<SiteProfile?> GetByIdAsync(int id);
    Task<SiteProfile> CreateAsync(SiteProfileRequest request);
    Task<bool> UpdateAsync(int siteId, SiteProfileUpdateRequest request);
}
