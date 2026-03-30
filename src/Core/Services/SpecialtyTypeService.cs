using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

public class SpecialtyTypeService : ISpecialtyTypeService
{
    private readonly IRepository<SpecialtyType> _repository;
    private readonly ILogger<SpecialtyTypeService> _logger;

    public SpecialtyTypeService(
        ILogger<SpecialtyTypeService> logger,
        IRepository<SpecialtyType> repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async Task<IEnumerable<SpecialtyTypeInfo>> GetAllAsync()
    {
        _logger.LogInformation("Getting all specialty types");
        var specialties = await _repository.GetAllAsync();
        return specialties.Select(MapToInfo);
    }

    public async Task<SpecialtyTypeInfo?> GetByIdAsync(int id)
    {
        _logger.LogInformation("Getting specialty type by ID: {Id}", id);
        var specialty = await _repository.GetByIdAsync(id);
        return specialty != null ? MapToInfo(specialty) : null;
    }

    public async Task<SpecialtyTypeInfo> CreateAsync(SpecialtyTypeCreateRequest request)
    {
        _logger.LogInformation("Creating new specialty type");
        var entity = new SpecialtyType
        {
            SpecialtyAbbreviation = request.Abbreviation,
            SpecialtyName = request.Name,
        };
        var created = await _repository.AddAsync(entity);
        _logger.LogInformation("Created specialty type: {Id}", created.Id);
        return MapToInfo(created);
    }

    public async Task<bool> UpdateAsync(int id, SpecialtyTypeUpdateRequest request)
    {
        _logger.LogInformation("Updating specialty type with ID: {Id}", id);
        ArgumentNullException.ThrowIfNull(request);

        var entity = await _repository.GetByIdAsync(id);
        if (entity == null) return false;

        if (request.Abbreviation != null) entity.SpecialtyAbbreviation = request.Abbreviation;
        if (request.Name != null) entity.SpecialtyName = request.Name;

        await _repository.UpdateAsync(entity);
        return true;
    }

    private static SpecialtyTypeInfo MapToInfo(SpecialtyType entity)
    {
        return new SpecialtyTypeInfo
        {
            SpecialtyId = entity.Id,
            Abbreviation = entity.SpecialtyAbbreviation,
            Name = entity.SpecialtyName,
        };
    }
}
