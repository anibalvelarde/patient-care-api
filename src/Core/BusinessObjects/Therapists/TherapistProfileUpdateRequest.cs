using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Therapists;

public class TherapistProfileUpdateRequest
{
    public TherapistProfileUpdateRequest()
    {
        this.FirstName = string.Empty;
        this.LastName = string.Empty;
        this.MiddleName = string.Empty;
        this.Email = string.Empty;
        this.PhoneNumber = string.Empty;
        this.Gender = string.Empty;
        this.DateOfBirth = DateTime.MinValue;
    }

    public string FirstName { get; set; }
    public string MiddleName { get; set; }
    public string LastName { get; set; }
    public DateTime DateOfBirth { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
    public string Gender { get; set; }
    public decimal? FeePctPerSession { get; set; }   
    public decimal? FeePerSession { get; set; }
    public bool ActiveStatus { get; set; }
    public override string ToString()
    {
        return $"FN: {this.FirstName} MN: {this.MiddleName} LN: {this.LastName} e: {this.Email} p: {this.PhoneNumber} DoB: {this.DateOfBirth.ToShortDateString()} Gender: {this.Gender} Fee: {this.FeePerSession} Fee %: {this.FeePctPerSession}";
    }

}
