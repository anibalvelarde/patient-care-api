using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.Entities;

public class User : PersonBase
{
    public User()
    {
        this.FirstName = string.Empty;
        this.LastName = string.Empty;
        this.MiddleName = string.Empty;
        this.Email = string.Empty;
        this.PhoneNumber = string.Empty;
        this.ActiveStatus = false;
        this.CreatedTimestamp = DateTime.UtcNow;
    }

    public string FirstName { get; set; }
    public string MiddleName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
    public bool ActiveStatus { get; set; }

    public string GetFullName()
    {
        return $"{LastName.Trim()}, {FirstName.Trim()} {MiddleName.Trim()}".Trim();
    }
    
    public override string ToString()
    {
        return $"ID: {this.Id} FN: {this.FirstName} MN: {this.MiddleName} LN: {this.LastName} e: {this.Email} p: {this.PhoneNumber} cr: {this.CreatedTimestamp.ToShortDateString()} Active: {this.ActiveStatus}";
    }
}