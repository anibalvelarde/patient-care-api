namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

public class DiscoverySessionSummary
{
    public int SessionId { get; set; }
    public DateOnly SessionDate { get; set; }
    public string SpecialtyAbbreviation { get; set; } = string.Empty;
    /// <summary>WP-61: the plan built from this discovery is credited to this therapist.</summary>
    public int TherapistId { get; set; }
    public string TherapistName { get; set; } = string.Empty;
}
