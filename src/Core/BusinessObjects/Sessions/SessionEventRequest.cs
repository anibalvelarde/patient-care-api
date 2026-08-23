using Neurocorp.Api.Core.Services;

namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

public class SessionEventRequest
{
    public SessionEventRequest()
    {
        this.Notes = string.Empty;
    }

    public DateOnly SessionDate { get; set; }
    public TimeOnly SessionTime { get; set; }
    public int PatientId { get; set; }
    public int TherapistId { get; set; }
    public string TherapyType { get; set; } = "N/A";
    public int Duration { get; set; } = 60;
    public decimal Amount { get; set; }
    public decimal Discount { get; set; }
    public decimal ProviderAmount { get; set; }
    public bool IsPaidOff { get; set; } = false;
    public string Notes { get; set; }
    public int AppointmentStatusId { get; set; } = SessionStatus.Proposed;
    public int? SiteId { get; set; }
    public int? SpecialtyTypeId { get; set; }
    // WP-40 on-site leg: only valid when the resolved specialty is OfferedOnSite; requires
    // SiteId (the trip charge is configured per site and snapshotted at booking).
    public bool IsOnSiteVisit { get; set; }
}