namespace Neurocorp.Api.Core.BusinessObjects.Patients;

// WP-30 (U2): slim typeahead row for dropdown/picker consumers — never the full census (gate G3).
public class CaretakerLookupItem
{
    public int CaretakerId { get; set; }
    public string CaretakerName { get; set; } = string.Empty;
}
