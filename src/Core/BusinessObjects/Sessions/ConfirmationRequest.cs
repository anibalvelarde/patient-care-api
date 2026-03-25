namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

public class ConfirmationRequest
{
    public string ConfirmationMethod { get; set; } = "Phone";
    public string ConfirmationResult { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
