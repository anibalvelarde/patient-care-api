namespace Neurocorp.Api.Core.BusinessObjects.Payments;

public class PaymentTypeInfo
{
    public int PaymentTypeId { get; set; }
    public string Abbreviation { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
