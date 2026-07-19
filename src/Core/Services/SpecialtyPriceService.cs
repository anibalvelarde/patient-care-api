using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

/// <summary>
/// WP-39: specialty price-sheet history, append-only writes, and the shared price-resolution
/// helper (WP-40's dependency). Temporal rules live in <see cref="SpecialtyPricing"/> so this
/// service, the lookup projection, and WP-40 all agree.
/// </summary>
public class SpecialtyPriceService : ISpecialtyPriceService
{
    private readonly ISpecialtyPriceRepository _repository;
    private readonly ILogger<SpecialtyPriceService> _logger;

    public SpecialtyPriceService(
        ILogger<SpecialtyPriceService> logger,
        ISpecialtyPriceRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async Task<SpecialtyPriceHistory?> GetHistoryAsync(int specialtyTypeId)
    {
        var specialty = await _repository.GetWithPricesAsync(specialtyTypeId);
        if (specialty == null) return null;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // The current row per duration = the one CurrentEffective picks for today.
        var current = SpecialtyPricing.CurrentEffective(specialty.DurationPrices, today)
            .ToDictionary(p => p.DurationMinutes, p => p.EffectiveFrom);

        return new SpecialtyPriceHistory
        {
            SpecialtyTypeId = specialty.Id,
            DefaultAmount = specialty.DefaultAmount,
            OfferedOnSite = specialty.OfferedOnSite,
            Prices = specialty.DurationPrices
                .OrderBy(r => r.DurationMinutes)
                .ThenByDescending(r => r.EffectiveFrom) // newest-first per duration
                .Select(r => new SpecialtyPriceHistoryRow
                {
                    DurationMinutes = r.DurationMinutes,
                    Amount = r.Amount,
                    EffectiveFrom = r.EffectiveFrom,
                    IsCurrent = current.TryGetValue(r.DurationMinutes, out var effective) &&
                                effective == r.EffectiveFrom,
                })
                .ToList(),
        };
    }

    public async Task<PriceAppendResult> AppendAsync(int specialtyTypeId, SpecialtyPricesPutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var specialty = await _repository.GetWithPricesAsync(specialtyTypeId);
        if (specialty == null) return PriceAppendResult.SpecialtyNotFound;

        // 409 on any (duration, effectiveFrom) pair already on file — or repeated in-request.
        // History is append-only: a correction is a new row with a different effective date.
        var onFile = specialty.DurationPrices
            .Select(r => (r.DurationMinutes, r.EffectiveFrom))
            .ToHashSet();
        var incoming = new HashSet<(int, DateOnly)>();
        foreach (var row in request.Prices)
        {
            var key = (row.DurationMinutes, row.EffectiveFrom!.Value);
            if (onFile.Contains(key) || !incoming.Add(key))
            {
                _logger.LogInformation(
                    "Rejecting duplicate price row for specialty {SpecialtyTypeId}: {Duration} min effective {EffectiveFrom}",
                    specialtyTypeId, key.Item1, key.Item2);
                return PriceAppendResult.DuplicateRow;
            }
        }

        var entities = request.Prices
            .Select(r => new SpecialtyDurationPrice
            {
                SpecialtyTypeId = specialtyTypeId,
                DurationMinutes = r.DurationMinutes,
                Amount = r.Amount,
                EffectiveFrom = r.EffectiveFrom!.Value,
            })
            .ToList();

        await _repository.AddRangeAsync(entities);
        _logger.LogInformation("Appended {Count} price row(s) for specialty {SpecialtyTypeId}",
            entities.Count, specialtyTypeId);
        return PriceAppendResult.Success;
    }

    public async Task<PriceResolution> ResolvePriceAsync(int specialtyTypeId, int durationMinutes, DateOnly sessionDate)
    {
        var specialty = await _repository.GetWithPricesAsync(specialtyTypeId);
        if (specialty == null) return PriceResolution.None;

        return SpecialtyPricing.Resolve(specialty, durationMinutes, sessionDate);
    }
}
