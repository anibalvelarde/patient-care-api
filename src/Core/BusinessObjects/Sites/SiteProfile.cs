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
}
