namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

public class SessionEvent
{
    public SessionEvent()
    {
        this.Patient = string.Empty;
        this.Therapist = string.Empty;
        this.Notes = string.Empty;
    }

    public int SessionId { get; set; }
    public DateOnly SessionDate { get; set; }
    public TimeOnly SessionTime { get; set; }
    public string Patient { get; set; }
    public string Therapist { get; set; }
    public string? TherapyTypes { get; set; } = "N/A";
    public decimal Amount { get; set; }
    public decimal Discount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountDue { get; set; }
    public bool IsPastDue { get; set; }
    public bool IsPaidOff { get; set; }
    public string Notes { get; set; }
    public int PatientId { get; set; }
    public int TherapistId { get; set;}
}