namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

public class DiscoverySessionSummary
{
    public int SessionId { get; set; }
    public DateOnly SessionDate { get; set; }
    public string SpecialtyAbbreviation { get; set; } = string.Empty;
    public string TherapistName { get; set; } = string.Empty;
}
