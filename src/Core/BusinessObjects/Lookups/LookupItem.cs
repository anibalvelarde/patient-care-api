namespace Neurocorp.Api.Core.BusinessObjects.Lookups;

public class LookupItem
{
    public int Id { get; set; }
    public string Abbreviation { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedTimestamp { get; set; }
    public DateTime LastUpdatedTimestamp { get; set; }
}
