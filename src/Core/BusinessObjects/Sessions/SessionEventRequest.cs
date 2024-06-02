namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

public class SessionEventRequest
{
    public SessionEventRequest()
    {
        this.Notes = string.Empty;
    }

    public DateOnly SessionDate { get; set; }
    public int PatientId { get; set; }
    public int TherapistId { get; set; }
    public string TherapyType { get; set; } = "N/A";
    public int Duration { get; set; } = 60;
    public decimal Amount { get; set; }
    public decimal Discount { get; set; }
    public decimal ProviderAmount { get; set; }
    public bool IsPaidOff { get; set; } = false;
    public string Notes { get; set; }
}