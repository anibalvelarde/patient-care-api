namespace Neurocorp.Api.Core.Entities;

public class PaymentType
{
    public int Id { get; set; }
    public string PmtTypeAbbreviation { get; set; } = string.Empty;
    public string PmtTypeName { get; set; } = string.Empty;
}
