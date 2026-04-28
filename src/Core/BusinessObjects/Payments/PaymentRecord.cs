namespace Neurocorp.Api.Core.BusinessObjects.Payments;

public class PaymentRecord
{
    public PaymentRecord()
    {
        CaretakerName = string.Empty;
        PaymentType = new PaymentTypeInfo();
        Allocations = new List<AllocationDetail>();
    }

    public int PaymentId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public int CaretakerId { get; set; }
    public string CaretakerName { get; set; }
    public PaymentTypeInfo PaymentType { get; set; }
    public string? CheckNumber { get; set; }
    public List<AllocationDetail> Allocations { get; set; }
    public decimal TotalAllocated { get; set; }
    public decimal UnallocatedAmount { get; set; }
}
