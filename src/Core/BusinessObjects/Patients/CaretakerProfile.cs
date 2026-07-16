using Neurocorp.Api.Core.BusinessObjects.Common;

namespace Neurocorp.Api.Core.BusinessObjects.Patients;

public class CaretakerProfile : IHasAudit
{
    public CaretakerProfile()
    {
        this.CaretakerName = string.Empty;
        this.Notes = string.Empty;
    }

    public int CaretakerId { get; set; }
    public int UserId { get; set; }
    public string CaretakerName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string Notes { get; set; }
    public DateTime CreatedTimestamp { get; set; }
    public DateTime LastUpdated { get; set; }
    public bool IsActive { get; set; }
    // WP-31 (U1): additive audit block for the row ⓘ popover.
    public AuditInfo? Audit { get; set; }
    public List<CaretakerPatientSummary> Patients { get; set; } = new();
}
