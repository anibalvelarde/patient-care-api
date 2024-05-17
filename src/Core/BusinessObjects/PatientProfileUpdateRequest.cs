using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects;

public class PatientProfileUpdateRequest
{
    public PatientProfileUpdateRequest()
    {
        this.FirstName = string.Empty;
        this.LastName = string.Empty;
        this.MiddleName = string.Empty;
        this.Email = string.Empty;
        this.PhoneNumber = string.Empty;
        this.Gender = string.Empty;
        this.DateOfBirth = DateTime.MinValue;
        this.ActiveStatus = true;
    }

    public string FirstName { get; set; }
    public string MiddleName { get; set; }
    public string LastName { get; set; }
    public DateTime DateOfBirth { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
    public string Gender { get; set; }
    public bool ActiveStatus { get; set; }

    public override string ToString()
    {
        return $"FN: {this.FirstName} MN: {this.MiddleName} LN: {this.LastName} e: {this.Email} p: {this.PhoneNumber} DoB: {this.DateOfBirth.ToShortDateString()} Gender: {this.Gender} Active: {this.ActiveStatus}";
    }

}
