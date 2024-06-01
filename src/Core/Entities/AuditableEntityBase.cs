using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.Entities;

public abstract class AuditableEntityBase
{
    [Key]
    public int Id { get; set; }
    public DateTime CreatedTimestamp { get; set; }
    public DateTime LastUpdatedTimestamp { get; set; }
    public int LastUpdatedByUserId  { get; set; }
}