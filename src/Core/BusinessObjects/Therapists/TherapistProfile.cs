using Neurocorp.Api.Core.BusinessObjects.Common;

namespace Neurocorp.Api.Core.BusinessObjects.Therapists;

public class TherapistProfile : IProfile
{
    public TherapistProfile()
    {
        TherapistName = string.Empty;
        Email = string.Empty;
        PhoneNumber = string.Empty;
    }

    public int TherapistId { get; set; }
    public int UserId { get; set; }
    public decimal FeePerSession { get; set; }
    public decimal FeePctPerSession { get; set; }
    public string TherapistName { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
    public DateTime CreatedTimestamp    { get; set; }
    public bool IsActive { get; set; }

    int IProfile.Id => this.TherapistId;
    string IProfile.Name => this.TherapistName;
    bool IProfile.IsValid => true;

    internal decimal CalculateFee(decimal amount)
    {
        if (FeePctPerSession.Equals(0))
        {            
            return FeePerSession;  // flat fee!
        }
        return amount * FeePctPerSession;  // % of business!
    }
}