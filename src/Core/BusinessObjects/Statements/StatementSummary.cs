namespace Neurocorp.Api.Core.BusinessObjects.Statements;

public class StatementSummary
{
    public decimal TotalCharges { get; set; }
    public decimal TotalDiscounts { get; set; }
    public decimal TotalNetCharges { get; set; }
    public decimal TotalPayments { get; set; }
    public decimal OutstandingBalance { get; set; }
    public decimal PastDueAmount { get; set; }
    public int PastDueSessionCount { get; set; }
}
