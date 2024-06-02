using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

public class SessionEventUpdateRequest
{
    public SessionEventUpdateRequest()
    {
        this.Notes = string.Empty;
    }

    public DateOnly? SessionDate { get; set; }
    public string TherapyType { get; set; } = "N/A";
    public int Duration { get; set; }
    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Discount { get; set; }
    public decimal ProviderAmount { get; set; }
    public bool IsPaidOff { get; set; } = false;
    public string Notes { get; set; }
}