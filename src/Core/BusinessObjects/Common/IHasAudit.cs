namespace Neurocorp.Api.Core.BusinessObjects.Common;

/// <summary>
/// WP-31 (U1): profiles/events that expose the additive <see cref="AuditInfo"/> block. The shared
/// interface lets name resolution enrich a mixed batch of DTOs after materialization.
/// </summary>
public interface IHasAudit
{
    AuditInfo? Audit { get; set; }
}
