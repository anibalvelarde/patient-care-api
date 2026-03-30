using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Payments;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

public class PaymentTypeService : IPaymentTypeService
{
    private readonly IRepository<PaymentType> _repository;
    private readonly ILogger<PaymentTypeService> _logger;

    public PaymentTypeService(
        ILogger<PaymentTypeService> logger,
        IRepository<PaymentType> repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async Task<IEnumerable<PaymentTypeInfo>> GetAllAsync()
    {
        _logger.LogInformation("Getting all payment types");
        var types = await _repository.GetAllAsync();
        return types.Select(MapToInfo);
    }

    public async Task<PaymentTypeInfo?> GetByIdAsync(int id)
    {
        _logger.LogInformation("Getting payment type by ID: {Id}", id);
        var type = await _repository.GetByIdAsync(id);
        return type != null ? MapToInfo(type) : null;
    }

    public async Task<PaymentTypeInfo> CreateAsync(PaymentTypeCreateRequest request)
    {
        _logger.LogInformation("Creating new payment type");
        var entity = new PaymentType
        {
            PmtTypeAbbreviation = request.Abbreviation,
            PmtTypeName = request.Name,
        };
        var created = await _repository.AddAsync(entity);
        _logger.LogInformation("Created payment type: {Id}", created.Id);
        return MapToInfo(created);
    }

    public async Task<bool> UpdateAsync(int id, PaymentTypeUpdateRequest request)
    {
        _logger.LogInformation("Updating payment type with ID: {Id}", id);
        ArgumentNullException.ThrowIfNull(request);

        var entity = await _repository.GetByIdAsync(id);
        if (entity == null) return false;

        if (request.Abbreviation != null) entity.PmtTypeAbbreviation = request.Abbreviation;
        if (request.Name != null) entity.PmtTypeName = request.Name;

        await _repository.UpdateAsync(entity);
        return true;
    }

    private static PaymentTypeInfo MapToInfo(PaymentType entity)
    {
        return new PaymentTypeInfo
        {
            PaymentTypeId = entity.Id,
            Abbreviation = entity.PmtTypeAbbreviation,
            Name = entity.PmtTypeName,
        };
    }
}
