using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.Entities;

public class User
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

    [Key]
    public int UserId { get; set; }
    public string FirstName { get; set; }
    public string MiddleName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
    public DateTime CreatedTimestamp { get; set; }
    public bool ActiveStatus { get; set; } 
}