namespace Neurocorp.Api.Core.BusinessObjects.Statements;

public class TherapistStatementPatient
{
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public List<TherapistStatementSession> Sessions { get; set; } = new();
    public decimal SubtotalFee { get; set; }
    public decimal SubtotalDiscount { get; set; }
    public decimal SubtotalProviderAmount { get; set; }
    public int CompletedCount { get; set; }
    public int NonBillableCount { get; set; }
}
