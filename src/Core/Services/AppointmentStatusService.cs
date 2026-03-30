using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

public class AppointmentStatusService : IAppointmentStatusService
{
    private readonly IRepository<AppointmentStatus> _repository;
    private readonly ILogger<AppointmentStatusService> _logger;

    public AppointmentStatusService(
        ILogger<AppointmentStatusService> logger,
        IRepository<AppointmentStatus> repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async Task<IEnumerable<AppointmentStatusInfo>> GetAllAsync()
    {
        _logger.LogInformation("Getting all appointment statuses");
        var statuses = await _repository.GetAllAsync();
        return statuses.Select(MapToInfo);
    }

    public async Task<AppointmentStatusInfo?> GetByIdAsync(int id)
    {
        _logger.LogInformation("Getting appointment status by ID: {Id}", id);
        var status = await _repository.GetByIdAsync(id);
        return status != null ? MapToInfo(status) : null;
    }

    public async Task<AppointmentStatusInfo> CreateAsync(AppointmentStatusCreateRequest request)
    {
        _logger.LogInformation("Creating new appointment status");
        var entity = new AppointmentStatus
        {
            StatusName = request.StatusName,
            StatusDescription = request.StatusDescription,
        };
        var created = await _repository.AddAsync(entity);
        _logger.LogInformation("Created appointment status: {Id}", created.Id);
        return MapToInfo(created);
    }

    public async Task<bool> UpdateAsync(int id, AppointmentStatusUpdateRequest request)
    {
        _logger.LogInformation("Updating appointment status with ID: {Id}", id);
        ArgumentNullException.ThrowIfNull(request);

        var entity = await _repository.GetByIdAsync(id);
        if (entity == null) return false;

        if (request.StatusName != null) entity.StatusName = request.StatusName;
        if (request.StatusDescription != null) entity.StatusDescription = request.StatusDescription;

        await _repository.UpdateAsync(entity);
        return true;
    }

    private static AppointmentStatusInfo MapToInfo(AppointmentStatus entity)
    {
        return new AppointmentStatusInfo
        {
            AppointmentStatusId = entity.Id,
            StatusName = entity.StatusName,
            StatusDescription = entity.StatusDescription,
        };
    }
}
