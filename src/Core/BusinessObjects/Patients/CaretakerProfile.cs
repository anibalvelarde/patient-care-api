namespace Neurocorp.Api.Core.BusinessObjects.Patients;

public class CaretakerProfile
{
    public CaretakerProfile()
    {
        this.CaretakerName = string.Empty;
        this.Email = string.Empty;
        this.PhoneNumber = string.Empty;
        this.Notes = string.Empty;
    }

    public int CaretakerId { get; set; }
    public int UserId { get; set; }
    public string CaretakerName { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
    public string Notes { get; set; }
    public DateTime CreatedTimestamp { get; set; }
    public DateTime LastUpdated { get; set; }    
    public bool IsActive { get; set; }
}
