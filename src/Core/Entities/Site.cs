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

    /// <summary>
    /// WP-39 (G4): flat clinic-wide trip charge attached to a session booked as an on-site
    /// visit (WP-40 applies it; snapshots to TherapySession.OnSiteChargeAmount at booking).
    /// ≥ 0, API-enforced; 0 = no charge configured (DB default, V030).
    /// </summary>
    public decimal OnSiteTripChargeAmount { get; set; }

    public ICollection<TherapySession> TherapySessions { get; set; } = [];
}
