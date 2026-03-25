namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

public class ConfirmationRecord
{
    public int ConfirmationId { get; set; }
    public int SessionId { get; set; }
    public string ConfirmationMethod { get; set; } = string.Empty;
    public string ConfirmationResult { get; set; } = string.Empty;
    public DateTime? ConfirmedAt { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedTimestamp { get; set; }
}
