namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

public class AppointmentStatusInfo
{
    public int AppointmentStatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string? StatusDescription { get; set; }
}
