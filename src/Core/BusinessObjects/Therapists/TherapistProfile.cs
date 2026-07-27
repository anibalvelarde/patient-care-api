using Neurocorp.Api.Core.BusinessObjects.Common;

namespace Neurocorp.Api.Core.BusinessObjects.Therapists;

public class TherapistProfile : IProfile
{
    public TherapistProfile()
    {
        TherapistName = string.Empty;
    }

    public int TherapistId { get; set; }
    public int UserId { get; set; }
    public decimal FeePerSession { get; set; }
    public decimal FeePctPerSession { get; set; }
    public string TherapistName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime CreatedTimestamp    { get; set; }
    public bool IsActive { get; set; }
    public List<TherapistSpecialtyItem> Specialties { get; set; } = [];

    int IProfile.Id => this.TherapistId;
    string IProfile.Name => this.TherapistName;
    bool IProfile.IsValid => true;

    // WP-40: delegates to the shared derivation math so this path and BulkSchedulingService
    // apply the identical fee model (flat when pct is 0, else % of net).
    internal decimal CalculateFee(decimal amount)
        => Services.SessionMoneyMath.ProviderFee(FeePerSession, FeePctPerSession, amount);
}