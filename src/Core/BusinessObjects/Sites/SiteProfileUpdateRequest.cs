namespace Neurocorp.Api.Core.BusinessObjects.Sites;

public class SiteProfileUpdateRequest
{
    public string? SiteName { get; set; }
    public string? RUC { get; set; }
    public DateTime? InceptionDate { get; set; }
    public string? Address { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
}
