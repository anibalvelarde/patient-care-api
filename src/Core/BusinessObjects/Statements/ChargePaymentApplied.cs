namespace Neurocorp.Api.Core.BusinessObjects.Statements;

public class ChargePaymentApplied
{
    public int PaymentId { get; set; }
    public DateTime PaymentDate { get; set; }
    public decimal AmountAllocated { get; set; }
    public string PaymentType { get; set; } = string.Empty;
    public string? CheckNumber { get; set; }
}
