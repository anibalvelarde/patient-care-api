namespace Neurocorp.Api.Core.BusinessObjects.Therapists;

public class TherapistProfile
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
}