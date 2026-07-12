namespace Neurocorp.Api.Core.BusinessObjects.Lookups;

public class LookupItem
{
    public int Id { get; set; }
    public string Abbreviation { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    // WP-23 (F6): per-type field — populated only for specialty-types; null for every other table.
    public decimal? DefaultAmount { get; set; }
    public DateTime CreatedTimestamp { get; set; }
    public DateTime LastUpdatedTimestamp { get; set; }
}
