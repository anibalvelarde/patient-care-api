using Neurocorp.Api.Core.BusinessObjects.Sessions;

namespace Neurocorp.Api.Core.Entities;

public class AppointmentConfirmation : AuditableEntityBase
{
    public int SessionId { get; set; }
    public string ConfirmationMethod { get; set; } = ConfirmationValues.MethodPhone;
    public int? ConfirmedByUserId { get; set; }
    public string ConfirmationResult { get; set; } = string.Empty;
    public DateTime? ConfirmedAt { get; set; }
    public string? Notes { get; set; }

    public TherapySession? TherapySession { get; set; }
}
