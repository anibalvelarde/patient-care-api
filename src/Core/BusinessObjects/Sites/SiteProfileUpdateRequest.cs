using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Sites;

public class SiteProfileUpdateRequest
{
    public string? SiteName { get; set; }
    public string? RUC { get; set; }
    public DateTime? InceptionDate { get; set; }
    public string? Address { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    // WP-32 (U4): null/omitted = unchanged; 400 outside 0-480 (gate G3).
    [Range(0, 480)]
    public int? IdleLogoffMinutes { get; set; }

    // WP-39 (G4): null/omitted = unchanged; 400 if negative.
    [Range(0, 99999999.99)]
    public decimal? OnSiteTripChargeAmount { get; set; }

    // WP-42 (G1): null/omitted = unchanged; 400 outside 0-100; a CHANGED value requires the
    // SYSADMIN role (field-gate in SitesController; echoed-unchanged passes any role).
    // Double-typed bounds on purpose: the int overload truncates decimals (−0.01 would pass).
    [Range(0.0, 100.0)]
    public decimal? NoShowFeePct { get; set; }
}
