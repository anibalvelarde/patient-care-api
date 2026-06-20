namespace Neurocorp.Api.Core.BusinessObjects.ServicePayments;

/// <summary>One session's share of a service payment, as submitted on create.</summary>
public class SessionServiceAllocationItem
{
    public int SessionId { get; set; }
    public decimal AmountApplied { get; set; }
}
