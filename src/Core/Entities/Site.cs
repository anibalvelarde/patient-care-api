namespace Neurocorp.Api.Core.Entities;

public class Site : AuditableEntityBase
{
    public Site()
    {
        this.SiteName = string.Empty;
    }

    public string SiteName { get; set; }
    public string? RUC { get; set; }
    public DateTime InceptionDate { get; set; }
    public string? Address { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public ICollection<TherapySession> TherapySessions { get; set; } = [];
}
