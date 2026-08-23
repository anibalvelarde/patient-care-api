using Neurocorp.Api.Core.Services;

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
    public int IdleLogoffMinutes { get; set; } = SiteDefaults.IdleLogoffMinutes;

    /// <summary>
    /// WP-39 (G4): flat clinic-wide trip charge attached to a session booked as an on-site
    /// visit (WP-40 applies it; snapshots to TherapySession.OnSiteChargeAmount at booking).
    /// ≥ 0, API-enforced; 0 = no charge configured (DB default, V030).
    /// </summary>
    public decimal OnSiteTripChargeAmount { get; set; }

    /// <summary>
    /// WP-42 (G1, V032): no-show fee as a percentage of the booked gross Amount, applied by
    /// the transition choke point when a session moves to NoShow. 0–100 API-enforced
    /// (0 = no fee); editing a CHANGED value is SYSADMIN-role-gated.
    /// Default raised 30 → 100 by WP-49/BR1 (V033) — see <see cref="SiteDefaults.NoShowFeePct"/>.
    /// </summary>
    public decimal NoShowFeePct { get; set; } = SiteDefaults.NoShowFeePct;

    public ICollection<TherapySession> TherapySessions { get; set; } = [];
}
