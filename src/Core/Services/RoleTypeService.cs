using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

public class RoleTypeService : IRoleTypeService
{
    private readonly IRepository<RoleType> _repository;
    private readonly ILogger<RoleTypeService> _logger;

    public RoleTypeService(
        ILogger<RoleTypeService> logger,
        IRepository<RoleType> repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async Task<IEnumerable<RoleTypeInfo>> GetAllAsync()
    {
        _logger.LogInformation("Getting all role types");
        var roles = await _repository.GetAllAsync();
        return roles.Select(MapToInfo);
    }

    public async Task<RoleTypeInfo?> GetByIdAsync(int id)
    {
        _logger.LogInformation("Getting role type by ID: {Id}", id);
        var role = await _repository.GetByIdAsync(id);
        return role != null ? MapToInfo(role) : null;
    }

    public async Task<RoleTypeInfo> CreateAsync(RoleTypeCreateRequest request)
    {
        _logger.LogInformation("Creating new role type");
        var entity = new RoleType
        {
            RoleName = request.RoleName,
        };
        var created = await _repository.AddAsync(entity);
        _logger.LogInformation("Created role type: {Id}", created.Id);
        return MapToInfo(created);
    }

    public async Task<bool> UpdateAsync(int id, RoleTypeUpdateRequest request)
    {
        _logger.LogInformation("Updating role type with ID: {Id}", id);
        ArgumentNullException.ThrowIfNull(request);

        var entity = await _repository.GetByIdAsync(id);
        if (entity == null) return false;

        if (request.RoleName != null) entity.RoleName = request.RoleName;

        await _repository.UpdateAsync(entity);
        return true;
    }

    private static RoleTypeInfo MapToInfo(RoleType entity)
    {
        return new RoleTypeInfo
        {
            RoleId = entity.Id,
            RoleName = entity.RoleName,
        };
    }
}
