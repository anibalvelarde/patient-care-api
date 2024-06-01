using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Patients;

public class CaretakerProfileUpdateRequest
{
    public CaretakerProfileUpdateRequest()
    {
        this.FirstName = string.Empty;
        this.LastName = string.Empty;
        this.MiddleName = string.Empty;
        this.Email = string.Empty;
        this.PhoneNumber = string.Empty;
        this.Notes = string.Empty;
    }

    public string FirstName { get; set; }
    public string MiddleName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
    public string Notes {get; set; }
    public bool IsActive { get; set; }

    public override string ToString()
    {
        return $"FN: {this.FirstName} MN: {this.MiddleName} LN: {this.LastName} e: {this.Email} p: {this.PhoneNumber}";
    }

}
