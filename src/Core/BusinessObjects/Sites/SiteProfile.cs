namespace Neurocorp.Api.Core.BusinessObjects.Sites;

public class SiteProfile
{
    public SiteProfile()
    {
        this.SiteName = string.Empty;
    }

    public int SiteId { get; set; }
    public string SiteName { get; set; }
    public string? RUC { get; set; }
    public DateTime InceptionDate { get; set; }
    public string? Address { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int IdleLogoffMinutes { get; set; } = 60;

    // WP-39 (G4): flat per-on-site-visit trip charge; 0 = no charge configured.
    public decimal OnSiteTripChargeAmount { get; set; }

    // WP-42 (G1): no-show fee as % of the booked gross Amount; 0 = no fee. Default 30 (V032).
    public decimal NoShowFeePct { get; set; } = 30.00m;
}
