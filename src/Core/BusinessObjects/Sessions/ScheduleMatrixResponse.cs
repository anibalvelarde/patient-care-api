namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

public class ScheduleMatrixResponse
{
    public DateOnly WeekStart { get; set; }
    public DateOnly WeekEnd { get; set; }
    public int SiteId { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public List<TherapistScheduleRow> Therapists { get; set; } = [];
}

public class TherapistScheduleRow
{
    public int TherapistId { get; set; }
    public string TherapistName { get; set; } = string.Empty;
    public List<string> Specialties { get; set; } = [];
    public List<ScheduleSlot> Slots { get; set; } = [];
}

public class ScheduleSlot
{
    public DateOnly Date { get; set; }
    public TimeOnly Time { get; set; }
    public int? SessionId { get; set; }
    public string? PatientName { get; set; }
    public string? SpecialtyAbbreviation { get; set; }
    public int? AppointmentStatusId { get; set; }
    public string? StatusName { get; set; }
}
