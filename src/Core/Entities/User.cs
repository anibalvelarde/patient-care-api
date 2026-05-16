using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.Entities;

public class User : PersonBase
{
    public User()
    {
        this.FirstName = string.Empty;
        this.LastName = string.Empty;
        this.ActiveStatus = false;
        this.CreatedTimestamp = DateTime.UtcNow;
    }

    public string FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string LastName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public bool ActiveStatus { get; set; }

    public string GetFullName()
    {
        var middle = MiddleName?.Trim();
        var head = $"{LastName.Trim()}, {FirstName.Trim()}";
        return string.IsNullOrEmpty(middle) ? head : $"{head} {middle}";
    }
    
    public override string ToString()
    {
        return $"ID: {this.Id} FN: {this.FirstName} MN: {this.MiddleName ?? string.Empty} LN: {this.LastName} e: {this.Email ?? string.Empty} p: {this.PhoneNumber ?? string.Empty} cr: {this.CreatedTimestamp.ToShortDateString()} Active: {this.ActiveStatus}";
    }
}