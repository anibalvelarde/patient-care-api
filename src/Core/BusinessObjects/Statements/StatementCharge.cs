namespace Neurocorp.Api.Core.BusinessObjects.Statements;

public class StatementCharge
{
    public int SessionId { get; set; }
    public string SessionDate { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string TherapistName { get; set; } = string.Empty;
    public string TherapyType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal NetCharge { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountDue { get; set; }
    public bool IsPastDue { get; set; }
    public List<ChargePaymentApplied> PaymentsApplied { get; set; } = new();
}
