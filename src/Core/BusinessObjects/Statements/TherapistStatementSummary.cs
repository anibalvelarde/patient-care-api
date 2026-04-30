namespace Neurocorp.Api.Core.BusinessObjects.Statements;

public class TherapistStatementSummary
{
    public int TotalCompletedSessions { get; set; }
    public int TotalNonBillableSessions { get; set; }
    public decimal TotalFee { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalProviderAmount { get; set; }
    public decimal TotalServicePaymentsApplied { get; set; }
    public decimal EstimatedAmountDue { get; set; }
}
