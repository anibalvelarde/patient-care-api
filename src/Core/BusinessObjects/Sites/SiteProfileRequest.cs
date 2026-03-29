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
}
