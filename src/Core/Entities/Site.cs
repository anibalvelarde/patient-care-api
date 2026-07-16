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

    /// <summary>Idle auto-logoff for UI sessions, in minutes; 0 = disabled (WP-32, U4).</summary>
    public int IdleLogoffMinutes { get; set; } = 60;

    public ICollection<TherapySession> TherapySessions { get; set; } = [];
}
