namespace Neurocorp.Api.Core.BusinessObjects.Statements;

public class TherapistStatement
{
    public int TherapistId { get; set; }
    public string TherapistName { get; set; } = string.Empty;
    public string StatementDate { get; set; } = string.Empty;
    public string PeriodStart { get; set; } = string.Empty;
    public string PeriodEnd { get; set; } = string.Empty;
    public bool IsProForma { get; set; } = true;
    public List<TherapistStatementPatient> Patients { get; set; } = new();
    public List<ServicePaymentItem> ServicePayments { get; set; } = new();
    public TherapistStatementSummary Summary { get; set; } = new();
}
