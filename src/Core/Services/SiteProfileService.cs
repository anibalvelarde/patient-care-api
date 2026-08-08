using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Sites;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

public class SiteProfileService : ISiteProfileService
{
    private readonly ISiteRepository _repository;
    private readonly ILogger<SiteProfileService> _logger;

    public SiteProfileService(
        ILogger<SiteProfileService> logger,
        ISiteRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async Task<IEnumerable<SiteProfile>> GetAllAsync()
    {
        _logger.LogInformation("Getting all site profiles");
        var sites = await _repository.GetAllAsync();
        return sites.Select(MapToProfile);
    }

    public async Task<SiteProfile?> GetByIdAsync(int id)
    {
        _logger.LogInformation("Getting site profile by ID: {id}", id);
        var site = await _repository.GetByIdAsync(id);
        return site != null ? MapToProfile(site) : null;
    }

    public async Task<SiteProfile> CreateAsync(SiteProfileRequest request)
    {
        _logger.LogInformation("Creating new site profile from request.");
        var newSite = await _repository.AddAsync(MapToEntity(request));
        _logger.LogInformation("New Site was created: SiteId[{SiteId}]", newSite.Id);
        return MapToProfile(newSite);
    }

    public async Task<bool> UpdateAsync(int siteId, SiteProfileUpdateRequest request)
    {
        _logger.LogInformation("Updating site profile with ID: {Id}", siteId);
        ArgumentNullException.ThrowIfNull(request);

        var site = await _repository.GetByIdAsync(siteId);
        if (site == null)
        {
            return false;
        }

        if (request.SiteName != null) site.SiteName = request.SiteName;
        if (request.RUC != null) site.RUC = request.RUC;
        if (request.InceptionDate.HasValue) site.InceptionDate = request.InceptionDate.Value;
        if (request.Address != null) site.Address = request.Address;
        if (request.Latitude.HasValue) site.Latitude = request.Latitude.Value;
        if (request.Longitude.HasValue) site.Longitude = request.Longitude.Value;
        if (request.IdleLogoffMinutes.HasValue) site.IdleLogoffMinutes = request.IdleLogoffMinutes.Value;
        if (request.OnSiteTripChargeAmount.HasValue) site.OnSiteTripChargeAmount = request.OnSiteTripChargeAmount.Value;
        if (request.NoShowFeePct.HasValue) site.NoShowFeePct = request.NoShowFeePct.Value;

        await _repository.UpdateAsync(site);
        return true;
    }

    private static SiteProfile MapToProfile(Site site)
    {
        return new SiteProfile
        {
            SiteId = site.Id,
            SiteName = site.SiteName,
            RUC = site.RUC,
            InceptionDate = site.InceptionDate,
            Address = site.Address,
            Latitude = site.Latitude,
            Longitude = site.Longitude,
            IdleLogoffMinutes = site.IdleLogoffMinutes,
            OnSiteTripChargeAmount = site.OnSiteTripChargeAmount,
            NoShowFeePct = site.NoShowFeePct,
        };
    }

    private static Site MapToEntity(SiteProfileRequest request)
    {
        return new Site
        {
            SiteName = request.SiteName,
            RUC = request.RUC,
            InceptionDate = request.InceptionDate,
            Address = request.Address,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            // Optional on create; the DB default is also 60 (V029).
            IdleLogoffMinutes = request.IdleLogoffMinutes ?? 60,
            // WP-39 (G4): optional on create; the DB default is also 0 (V030).
            OnSiteTripChargeAmount = request.OnSiteTripChargeAmount ?? 0m,
            // WP-42 (G1): optional on create; the DB default matches (V032, raised by V033).
            NoShowFeePct = request.NoShowFeePct ?? SiteDefaults.NoShowFeePct,
        };
    }
}
