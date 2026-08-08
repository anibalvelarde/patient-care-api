namespace Neurocorp.Api.Core.BusinessObjects.Statements;

public class StatementSummary
{
    public decimal TotalCharges { get; set; }
    public decimal TotalDiscounts { get; set; }
    public decimal TotalNetCharges { get; set; }

    /// <summary>WP-40: total on-site trip charges across the statement's sessions.</summary>
    public decimal TotalOnSiteCharges { get; set; }

    /// <summary>WP-49 (BR3): total late chargebacks in force across the statement's sessions.</summary>
    public decimal TotalLateFees { get; set; }

    public decimal TotalPayments { get; set; }
    public decimal OutstandingBalance { get; set; }
    public decimal PastDueAmount { get; set; }
    public int PastDueSessionCount { get; set; }
}
