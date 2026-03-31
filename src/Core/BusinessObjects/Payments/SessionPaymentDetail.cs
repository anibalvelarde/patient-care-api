using Neurocorp.Api.Core.BusinessObjects.Lookups;

namespace Neurocorp.Api.Core.BusinessObjects.Payments;

public class SessionPaymentDetail
{
    public SessionPaymentDetail()
    {
        CaretakerName = string.Empty;
        PaymentType = new LookupItem();
    }

    public int SessionPaymentId { get; set; }
    public int PaymentId { get; set; }
    public decimal AmountAllocated { get; set; }
    public DateTime PaymentDate { get; set; }
    public decimal PaymentTotalAmount { get; set; }
    public int CaretakerId { get; set; }
    public string CaretakerName { get; set; }
    public LookupItem PaymentType { get; set; }
    public string? CheckNumber { get; set; }
}
