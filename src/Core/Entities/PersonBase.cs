namespace Neurocorp.Api.Core.Entities;

public abstract class PersonBase : AuditableEntityBase
{
    public User? User { get; set; }
}