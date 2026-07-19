using System.ComponentModel.DataAnnotations;

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
    [Range(0, 480)]
    public int? IdleLogoffMinutes { get; set; }

    // WP-39 (G4): optional on create; defaults to 0 (no charge) when omitted. 400 if negative.
    [Range(0, 99999999.99)]
    public decimal? OnSiteTripChargeAmount { get; set; }
}
