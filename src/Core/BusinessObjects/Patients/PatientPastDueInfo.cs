using Neurocorp.Api.Core.BusinessObjects.Common;

namespace Neurocorp.Api.Core.BusinessObjects.Patients;

public class PatientPastDueInfo : PastDueInfo
{
    public override string PartyType => "Patient";
}