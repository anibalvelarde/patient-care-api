using System.Text;

namespace Neurocorp.Api.Core.Entities;

public class UserRole
{
    public UserRole()
    {
        RoleCreatedOn = DateTime.UtcNow;
    }

    public int UserRoleId { get; set; }
    public int RoleId { get; set; }
    public int UserId { get; set; }
    public DateTime? RoleCreatedOn { get; set; }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append("Uid: ").Append(this.UserId).Append("  ")
        .Append("Rid: ").Append(this.RoleId).Append("  ")
        .Append("Since: ").Append((this.RoleCreatedOn ?? DateTime.MinValue).ToShortDateString()).Append("  ")
        .Append("At: ").Append((this.RoleCreatedOn ?? DateTime.MinValue).ToShortTimeString()).Append("  ");
        return sb.ToString();
    }
}
