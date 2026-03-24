namespace Neurocorp.Api.Core.BusinessObjects.Statements;

public class StatementPayment
{
    public int PaymentId { get; set; }
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string PaymentType { get; set; } = string.Empty;
    public string? CheckNumber { get; set; }
    public List<StatementPaymentAllocation> Allocations { get; set; } = new();
}
