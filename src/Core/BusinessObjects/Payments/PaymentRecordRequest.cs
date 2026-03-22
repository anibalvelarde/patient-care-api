namespace Neurocorp.Api.Core.BusinessObjects.Payments;

public class PaymentRecordRequest
{
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public int CaretakerId { get; set; }
    public int PaymentTypeId { get; set; }
    public string? CheckNumber { get; set; }
    public List<SessionAllocationItem> SessionAllocations { get; set; } = new();
}
