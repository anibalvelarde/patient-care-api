using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Sessions;

namespace Neurocorp.Api.Core.BusinessObjects.Common;

public class PastDueInfo
{
    public virtual string PartyType { get; } = string.Empty;
    public IProfile? Party { get; set; }
    public int PastDueSessions { get; set; }
    public decimal? PastDueTotalAmount { get; set; }
    public decimal? AmountPaidSoFar { get; set; }
    public IEnumerable<SessionEvent>? Delinquency { get; set;}
}