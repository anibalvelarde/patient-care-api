namespace Neurocorp.Api.Core.Entities;

public class AppointmentStatus
{
    public int Id { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string? StatusDescription { get; set; }
}
