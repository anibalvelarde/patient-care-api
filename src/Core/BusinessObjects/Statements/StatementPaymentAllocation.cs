namespace Neurocorp.Api.Core.BusinessObjects.Statements;

public class StatementPaymentAllocation
{
    public int SessionId { get; set; }
    public string SessionDate { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public decimal AmountAllocated { get; set; }
}
