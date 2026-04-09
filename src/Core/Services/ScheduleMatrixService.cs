using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

public class ScheduleMatrixService : IScheduleMatrixService
{
    private readonly ILogger<ScheduleMatrixService> _logger;
    private readonly ITherapySessionRepository _sessionRepository;
    private readonly ITherapistRepository _therapistRepository;
    private readonly ITherapistSpecialtyRepository _specialtyRepository;
    private readonly IRepository<Site> _siteRepository;

    private static readonly TimeOnly SlotStart = new(8, 0);
    private static readonly TimeOnly SlotEnd = new(17, 0);

    public ScheduleMatrixService(
        ILogger<ScheduleMatrixService> logger,
        ITherapySessionRepository sessionRepository,
        ITherapistRepository therapistRepository,
        ITherapistSpecialtyRepository specialtyRepository,
        IRepository<Site> siteRepository)
    {
        _logger = logger;
        _sessionRepository = sessionRepository;
        _therapistRepository = therapistRepository;
        _specialtyRepository = specialtyRepository;
        _siteRepository = siteRepository;
    }

    public async Task<ScheduleMatrixResponse> GetMatrixAsync(DateOnly date, int siteId)
    {
        _logger.LogInformation("Building schedule matrix for week of {Date}, SiteId:{SiteId}", date, siteId);

        var site = await _siteRepository.GetByIdAsync(siteId)
            ?? throw new ArgumentException($"Site {siteId} not found.");

        // Compute Monday–Saturday for the week containing the given date
        var weekStart = GetMonday(date);
        var weekEnd = weekStart.AddDays(5); // Saturday

        // Fetch all non-cancelled sessions at this site for the week
        var sessions = await _sessionRepository.GetBySiteAndDateRangeAsync(siteId, weekStart, weekEnd);

        // Identify therapists: those with sessions this week + all therapists with specialties
        var therapistIdsFromSessions = sessions.Select(s => s.TherapistId).Distinct().ToHashSet();
        var allTherapists = await _therapistRepository.GetAllAsync();
        var relevantTherapists = allTherapists
            .Where(t => therapistIdsFromSessions.Contains(t.TherapistId) || t.TherapistSpecialties.Count > 0)
            .OrderBy(t => t.User?.LastName ?? "")
            .ThenBy(t => t.User?.FirstName ?? "")
            .ToList();

        // Pre-load therapist IDs for eager-loaded specialties
        var therapistIds = relevantTherapists.Select(t => t.TherapistId).ToList();
        var therapistsWithUser = await _therapistRepository.GetByIdsWithUserAsync(therapistIds);
        var therapistMap = therapistsWithUser.ToDictionary(t => t.TherapistId);

        // Group sessions by therapist+date+time for O(1) lookup
        var sessionLookup = sessions
            .GroupBy(s => (s.TherapistId, s.SessionDate, s.SessionTime.Hour))
            .ToDictionary(g => g.Key, g => g.First());

        // Build rows
        var rows = new List<TherapistScheduleRow>();
        foreach (var therapist in relevantTherapists)
        {
            var t = therapistMap.GetValueOrDefault(therapist.TherapistId) ?? therapist;
            var name = t.User != null
                ? $"{t.User.LastName}, {t.User.FirstName}".Trim().TrimEnd(',')
                : $"Therapist #{t.TherapistId}";

            var specialties = t.TherapistSpecialties
                .Where(ts => ts.SpecialtyType != null)
                .Select(ts => ts.SpecialtyType!.Abbreviation)
                .OrderBy(a => a)
                .ToList();

            var slots = BuildSlots(t.TherapistId, weekStart, weekEnd, sessionLookup);

            rows.Add(new TherapistScheduleRow
            {
                TherapistId = t.TherapistId,
                TherapistName = name,
                Specialties = specialties,
                Slots = slots,
            });
        }

        return new ScheduleMatrixResponse
        {
            WeekStart = weekStart,
            WeekEnd = weekEnd,
            SiteId = siteId,
            SiteName = site.SiteName,
            Therapists = rows,
        };
    }

    private static List<ScheduleSlot> BuildSlots(
        int therapistId,
        DateOnly weekStart,
        DateOnly weekEnd,
        Dictionary<(int TherapistId, DateOnly Date, int Hour), TherapySession> sessionLookup)
    {
        var slots = new List<ScheduleSlot>();

        for (var day = weekStart; day <= weekEnd; day = day.AddDays(1))
        {
            for (var time = SlotStart; time < SlotEnd; time = time.AddHours(1))
            {
                var key = (therapistId, day, time.Hour);
                var session = sessionLookup.GetValueOrDefault(key);

                slots.Add(new ScheduleSlot
                {
                    Date = day,
                    Time = time,
                    SessionId = session?.Id,
                    PatientName = session?.Patient?.User != null
                        ? $"{session.Patient.User.LastName}, {session.Patient.User.FirstName[0]}."
                        : null,
                    SpecialtyAbbreviation = session?.SpecialtyType?.Abbreviation,
                    AppointmentStatusId = session?.AppointmentStatusId,
                    StatusName = session?.AppointmentStatus?.Name,
                });
            }
        }

        return slots;
    }

    private static DateOnly GetMonday(DateOnly date)
    {
        var dayOfWeek = (int)date.DayOfWeek;
        // DayOfWeek: Sunday=0, Monday=1, ..., Saturday=6
        var daysToSubtract = dayOfWeek == 0 ? 6 : dayOfWeek - 1;
        return date.AddDays(-daysToSubtract);
    }
}
