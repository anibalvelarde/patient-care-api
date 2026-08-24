using System.ComponentModel.DataAnnotations;
using Neurocorp.Api.Core.Services;

namespace Neurocorp.Api.Core.BusinessObjects.Sites;

public class SiteProfileRequest
{
    public SiteProfileRequest()
    {
        this.SiteName = string.Empty;
    }

    [Required]
    public string SiteName { get; set; }
    public string? RUC { get; set; }
    [Required]
    public DateTime InceptionDate { get; set; }
    public string? Address { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    // WP-32 (U4): optional on create; defaults to 60 when omitted. 0 = disabled.
    // WP-55 B-2c: upper bound sourced from SiteDefaults (was a bare 480 literal).
    [Range(0, SiteDefaults.IdleLogoffMaxMinutes)]
    public int? IdleLogoffMinutes { get; set; }

    // WP-39 (G4): optional on create; defaults to 0 (no charge) when omitted. 400 if negative.
    [Range(0, 99999999.99)]
    public decimal? OnSiteTripChargeAmount { get; set; }

    // WP-42 (G1): optional on create; defaults to 30 when omitted. 400 outside 0-100; a
    // NON-DEFAULT value additionally requires the SYSADMIN role (gate in SitesController).
    // Double-typed bounds on purpose: the int overload truncates decimals (−0.01 would pass).
    [Range(0.0, 100.0)]
    public decimal? NoShowFeePct { get; set; }
}
