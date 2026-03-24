namespace Neurocorp.Api.Core.BusinessObjects.Statements;

public class AccountStatement
{
    public int CaretakerId { get; set; }
    public string CaretakerName { get; set; } = string.Empty;
    public string StatementDate { get; set; } = string.Empty;
    public string PeriodStart { get; set; } = string.Empty;
    public string PeriodEnd { get; set; } = string.Empty;
    public List<StatementPatient> Patients { get; set; } = new();
    public List<StatementCharge> Charges { get; set; } = new();
    public List<StatementPayment> Payments { get; set; } = new();
    public StatementSummary Summary { get; set; } = new();
}
