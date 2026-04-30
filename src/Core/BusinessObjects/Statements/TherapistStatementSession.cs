namespace Neurocorp.Api.Core.BusinessObjects.Statements;

public class TherapistStatementSession
{
    public int SessionId { get; set; }
    public string SessionDate { get; set; } = string.Empty;
    public string SessionTime { get; set; } = string.Empty;
    public string TherapyType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ProviderAmount { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public bool IsBillable { get; set; }
}
