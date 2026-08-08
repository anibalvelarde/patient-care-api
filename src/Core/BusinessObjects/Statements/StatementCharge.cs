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

    /// <summary>Service charge only: <c>Amount − DiscountAmount</c>. Excludes the add-on charges below.</summary>
    public decimal NetCharge { get; set; }

    /// <summary>WP-40: on-site trip charge on this session; null = in-clinic.</summary>
    public decimal? OnSiteChargeAmount { get; set; }

    /// <summary>
    /// WP-49 (BR3): late chargeback currently in force; null = none. Itemized so a caretaker
    /// reading the statement sees WHY the balance is larger than the service charge, instead of
    /// an unexplained number. <c>0.00</c> means it was applied and then waived.
    /// </summary>
    public decimal? LateFeeAmount { get; set; }

    public decimal AmountPaid { get; set; }

    /// <summary>
    /// The collectible balance. Reconciles as
    /// <c>NetCharge + OnSiteChargeAmount + LateFeeAmount − AmountPaid</c> — all three add-on
    /// terms must be displayed for the arithmetic to be followable.
    /// </summary>
    public decimal AmountDue { get; set; }

    public bool IsPastDue { get; set; }
    public List<ChargePaymentApplied> PaymentsApplied { get; set; } = new();
}
