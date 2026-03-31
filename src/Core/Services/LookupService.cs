using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

public class LookupService : ILookupService
{
    private static readonly Dictionary<string, Type> LookupTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["appointment-statuses"] = typeof(AppointmentStatus),
        ["payment-types"] = typeof(PaymentType),
        ["role-types"] = typeof(RoleType),
        ["specialty-types"] = typeof(SpecialtyType),
    };

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LookupService> _logger;

    public LookupService(IServiceProvider serviceProvider, ILogger<LookupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public bool IsValidTableName(string tableName)
    {
        return LookupTypes.ContainsKey(tableName);
    }

    public async Task<IEnumerable<LookupItem>> GetAllAsync(string tableName)
    {
        var entityType = ResolveType(tableName);
        var method = GetType()
            .GetMethod(nameof(GetAllInternalAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(entityType);
        return await (Task<IEnumerable<LookupItem>>)method.Invoke(this, null)!;
    }

    public async Task<LookupItem?> GetByIdAsync(string tableName, int id)
    {
        var entityType = ResolveType(tableName);
        var method = GetType()
            .GetMethod(nameof(GetByIdInternalAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(entityType);
        return await (Task<LookupItem?>)method.Invoke(this, new object[] { id })!;
    }

    public async Task<LookupItem> CreateAsync(string tableName, LookupCreateRequest request)
    {
        var entityType = ResolveType(tableName);
        var method = GetType()
            .GetMethod(nameof(CreateInternalAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(entityType);
        return await (Task<LookupItem>)method.Invoke(this, new object[] { request })!;
    }

    public async Task<bool> UpdateAsync(string tableName, int id, LookupUpdateRequest request)
    {
        var entityType = ResolveType(tableName);
        var method = GetType()
            .GetMethod(nameof(UpdateInternalAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(entityType);
        return await (Task<bool>)method.Invoke(this, new object[] { id, request })!;
    }

    private Type ResolveType(string tableName)
    {
        if (!LookupTypes.TryGetValue(tableName, out var entityType))
        {
            throw new ArgumentException($"Invalid lookup table name: {tableName}");
        }
        return entityType;
    }

    private async Task<IEnumerable<LookupItem>> GetAllInternalAsync<T>() where T : LookupEntityBase
    {
        _logger.LogInformation("Retrieving all {EntityType} records", typeof(T).Name);
        var repository = _serviceProvider.GetRequiredService<IRepository<T>>();
        var entities = await repository.GetAllAsync();
        _logger.LogInformation("Retrieved {Count} {EntityType} records", entities.Count, typeof(T).Name);
        return entities.Select(MapToLookupItem);
    }

    private async Task<LookupItem?> GetByIdInternalAsync<T>(int id) where T : LookupEntityBase
    {
        _logger.LogInformation("Retrieving {EntityType} with ID: {Id}", typeof(T).Name, id);
        var repository = _serviceProvider.GetRequiredService<IRepository<T>>();
        var entity = await repository.GetByIdAsync(id);
        return entity == null ? null : MapToLookupItem(entity);
    }

    private async Task<LookupItem> CreateInternalAsync<T>(LookupCreateRequest request) where T : LookupEntityBase, new()
    {
        _logger.LogInformation("Creating new {EntityType}", typeof(T).Name);
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTime.UtcNow;
        var entity = new T
        {
            Abbreviation = request.Abbreviation,
            Name = request.Name,
            Description = request.Description,
            SortOrder = (short)request.SortOrder,
            CreatedTimestamp = now,
            LastUpdatedTimestamp = now,
        };

        var repository = _serviceProvider.GetRequiredService<IRepository<T>>();
        var created = await repository.AddAsync(entity);
        _logger.LogInformation("Created {EntityType} with ID: {Id}", typeof(T).Name, created.Id);
        return MapToLookupItem(created);
    }

    private async Task<bool> UpdateInternalAsync<T>(int id, LookupUpdateRequest request) where T : LookupEntityBase
    {
        _logger.LogInformation("Updating {EntityType} with ID: {Id}", typeof(T).Name, id);
        ArgumentNullException.ThrowIfNull(request);

        var repository = _serviceProvider.GetRequiredService<IRepository<T>>();
        var entity = await repository.GetByIdAsync(id);
        if (entity == null) return false;

        if (request.Abbreviation != null) entity.Abbreviation = request.Abbreviation;
        if (request.Name != null) entity.Name = request.Name;
        if (request.Description != null) entity.Description = request.Description;
        if (request.SortOrder != null) entity.SortOrder = (short)request.SortOrder.Value;
        entity.LastUpdatedTimestamp = DateTime.UtcNow;

        await repository.UpdateAsync(entity);
        _logger.LogInformation("Updated {EntityType} with ID: {Id}", typeof(T).Name, id);
        return true;
    }

    private static LookupItem MapToLookupItem(LookupEntityBase entity)
    {
        return new LookupItem
        {
            Id = entity.Id,
            Abbreviation = entity.Abbreviation,
            Name = entity.Name,
            Description = entity.Description,
            SortOrder = entity.SortOrder,
            CreatedTimestamp = entity.CreatedTimestamp,
            LastUpdatedTimestamp = entity.LastUpdatedTimestamp,
        };
    }
}
