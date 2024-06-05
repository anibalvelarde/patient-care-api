using Neurocorp.Api.Core.BusinessObjects.Common;

namespace Neurocorp.Api.Core.BusinessObjects.Therapists;

public class TherapistPastDueInfo : PastDueInfo
{
    public override string PartyType => "Therapist";
}