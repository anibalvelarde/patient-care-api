using System.Collections.Generic;

namespace Neurocorp.Api.Core.Entities;

public class Therapist : PersonBase
{
    public int TherapistId { get; set; }
    public int UserId { get; set; }
    public decimal FeePerSession { get; set; }
    public decimal FeePctPerSession { get; set; }
    public ICollection<TherapistSpecialty> TherapistSpecialties { get; set; } = [];

    public UserRole MintNewRole()
    {
        return new UserRole() {
            UserId = this.UserId,
            RoleId = 1 // as defined in table UserRole for Therapists
        };
    }
}

public class UndefinedTherapist : Therapist
{
}