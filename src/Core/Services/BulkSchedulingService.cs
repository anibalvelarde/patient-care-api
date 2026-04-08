using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

public class BulkSchedulingService : IBulkSchedulingService
{
    private const decimal HourlyRate = 65.00m;
    private const int CancelledStatusId = 3;
    private const int CompletedStatusId = 4;
    private const int ProposedStatusId = 1;

    private readonly ILogger<BulkSchedulingService> _logger;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly ITherapySessionRepository _sessionRepository;
    private readonly ITherapistRepository _therapistRepository;
    private readonly ITherapistSpecialtyRepository _specialtyRepository;

    public BulkSchedulingService(
        ILogger<BulkSchedulingService> logger,
        ITreatmentPlanRepository planRepository,
        ITherapySessionRepository sessionRepository,
        ITherapistRepository therapistRepository,
        ITherapistSpecialtyRepository specialtyRepository)
    {
        _logger = logger;
        _planRepository = planRepository;
        _sessionRepository = sessionRepository;
        _therapistRepository = therapistRepository;
        _specialtyRepository = specialtyRepository;
    }

    public async Task<BulkScheduleResult> ScheduleAsync(int planId, BulkScheduleRequest request)
    {
        _logger.LogInformation("Bulk scheduling for PlanId:{PlanId}, StartDate:{StartDate}", planId, request.StartDate);

        var plan = await _planRepository.GetByIdWithLinesAsync(planId)
            ?? throw new ArgumentException($"Treatment plan {planId} not found.");

        if (plan.PlanStatus != "Active")
            throw new ArgumentException($"Cannot schedule sessions for a plan with status '{plan.PlanStatus}'. Plan must be Active.");

        if (await _sessionRepository.HasSessionsForPlanAsync(planId))
            throw new ArgumentException("This plan already has scheduled sessions. Cancel existing sessions before re-scheduling.");

        // Build effective lines (plan lines merged with overrides)
        var overrideMap = request.LineOverrides.ToDictionary(o => o.TreatmentPlanLineId);
        var effectiveLines = plan.Lines.OrderBy(l => l.SortOrder).Select(line =>
        {
            overrideMap.TryGetValue(line.Id, out var ov);
            return new EffectiveLine
            {
                LineId = line.Id,
                SpecialtyTypeId = line.SpecialtyTypeId,
                SpecialtyAbbreviation = line.SpecialtyType?.Abbreviation ?? string.Empty,
                TherapistId = ov?.TherapistId ?? line.PreferredTherapistId,
                DayOfWeek = ov?.DayOfWeek ?? line.DayOfWeek,
                Time = ov?.Time ?? line.PreferredTime,
                Duration = line.Duration,
                DiscountAmount = ov?.DiscountAmount ?? 0m,
            };
        }).ToList();

        // Compute date range for the full schedule
        var endDate = request.StartDate.AddDays(plan.DurationWeeks * 7);

        // Collect all therapist IDs we might need (preferred + auto-assign candidates)
        var allSpecialtyIds = effectiveLines.Select(l => l.SpecialtyTypeId).Distinct().ToList();
        var qualifiedTherapistsBySpecialty = new Dictionary<int, IReadOnlyList<int>>();
        foreach (var specId in allSpecialtyIds)
        {
            qualifiedTherapistsBySpecialty[specId] = await _specialtyRepository.GetTherapistIdsBySpecialtyAsync(specId);
        }

        // Get patient's historical therapist IDs (for auto-assign preference)
        var patientTherapistHistory = await _sessionRepository.GetTherapistIdsForPatientAsync(plan.PatientId);

        // Collect all therapist IDs we'll need profiles for
        var allTherapistIds = new HashSet<int>();
        foreach (var line in effectiveLines)
        {
            if (line.TherapistId.HasValue) allTherapistIds.Add(line.TherapistId.Value);
        }
        foreach (var ids in qualifiedTherapistsBySpecialty.Values)
        {
            foreach (var id in ids) allTherapistIds.Add(id);
        }

        // Pre-fetch therapist profiles
        var therapists = await _therapistRepository.GetByIdsWithUserAsync(allTherapistIds);
        var therapistMap = therapists.ToDictionary(t => t.Id);

        // Pre-fetch existing sessions for all therapists over the date range
        var existingSessionsByTherapist = new Dictionary<int, List<(DateOnly Date, TimeOnly Start, int Duration)>>();
        foreach (var therapistId in allTherapistIds)
        {
            var sessions = await _sessionRepository.GetByTherapistAndDateRangeAsync(therapistId, request.StartDate, endDate);
            existingSessionsByTherapist[therapistId] = sessions
                .Select(s => (s.SessionDate, s.SessionTime, s.Duration))
                .ToList();
        }

        var result = new BulkScheduleResult { PlanId = planId };
        var sessionsToCreate = new List<TherapySession>();

        for (int week = 1; week <= plan.DurationWeeks; week++)
        {
            var weekStart = request.StartDate.AddDays((week - 1) * 7);

            foreach (var line in effectiveLines)
            {
                var targetDate = ComputeTargetDate(weekStart, line.DayOfWeek);
                var targetTime = line.Time ?? new TimeOnly(9, 0);

                // Determine therapist
                int? assignedTherapistId = line.TherapistId;

                if (!assignedTherapistId.HasValue)
                {
                    assignedTherapistId = AutoAssignTherapist(
                        line.SpecialtyTypeId, targetDate, targetTime, line.Duration,
                        qualifiedTherapistsBySpecialty, patientTherapistHistory,
                        existingSessionsByTherapist);
                }

                if (!assignedTherapistId.HasValue)
                {
                    result.Conflicts.Add(new ScheduleConflict
                    {
                        WeekNumber = week,
                        LineId = line.LineId,
                        Date = targetDate,
                        Reason = "No qualified therapist available at this time.",
                        SuggestedAlternatives = BuildAlternatives(
                            line.SpecialtyTypeId, targetDate, targetTime, line.Duration,
                            qualifiedTherapistsBySpecialty, existingSessionsByTherapist, therapistMap)
                    });
                    continue;
                }

                // Check for conflict with assigned therapist
                if (HasConflict(assignedTherapistId.Value, targetDate, targetTime, line.Duration, existingSessionsByTherapist))
                {
                    var therapistName = GetTherapistName(assignedTherapistId.Value, therapistMap);
                    result.Conflicts.Add(new ScheduleConflict
                    {
                        WeekNumber = week,
                        LineId = line.LineId,
                        Date = targetDate,
                        Reason = $"Therapist {therapistName} is already booked at {targetTime:HH:mm} on {targetDate}.",
                        SuggestedAlternatives = BuildAlternatives(
                            line.SpecialtyTypeId, targetDate, targetTime, line.Duration,
                            qualifiedTherapistsBySpecialty, existingSessionsByTherapist, therapistMap)
                    });
                    continue;
                }

                // Compute financials
                var amount = Math.Round(HourlyRate * line.Duration / 60m, 2);
                var discountAmount = Math.Min(line.DiscountAmount, amount);
                var netAmount = amount - discountAmount;
                var providerAmount = ComputeProviderAmount(assignedTherapistId.Value, netAmount, therapistMap);
                var grossProfit = netAmount - providerAmount;

                var session = new TherapySession
                {
                    PatientId = plan.PatientId,
                    TherapistId = assignedTherapistId.Value,
                    SessionDate = targetDate,
                    SessionTime = targetTime,
                    Duration = line.Duration,
                    Amount = amount,
                    DiscountAmount = discountAmount,
                    ProviderAmount = providerAmount,
                    GrossProfit = grossProfit,
                    TherapyTypes = line.SpecialtyAbbreviation,
                    AppointmentStatusId = ProposedStatusId,
                    SiteId = request.SiteId,
                    SpecialtyTypeId = line.SpecialtyTypeId,
                    TreatmentPlanLineId = line.LineId,
                    Notes = string.Empty,
                };
                sessionsToCreate.Add(session);

                // Update in-memory conflict map so subsequent iterations see this session
                if (!existingSessionsByTherapist.ContainsKey(assignedTherapistId.Value))
                    existingSessionsByTherapist[assignedTherapistId.Value] = [];
                existingSessionsByTherapist[assignedTherapistId.Value]
                    .Add((targetDate, targetTime, line.Duration));

                result.Sessions.Add(new CreatedSessionInfo
                {
                    TreatmentPlanLineId = line.LineId,
                    WeekNumber = week,
                    SessionDate = targetDate,
                    SessionTime = targetTime,
                    TherapistId = assignedTherapistId.Value,
                    TherapistName = GetTherapistName(assignedTherapistId.Value, therapistMap),
                    SpecialtyAbbreviation = line.SpecialtyAbbreviation,
                    Amount = amount,
                    DiscountAmount = discountAmount,
                    ProviderAmount = providerAmount,
                    GrossProfit = grossProfit,
                });
            }
        }

        // Only persist if there are zero conflicts (atomic: all-or-nothing)
        if (result.Conflicts.Count > 0)
        {
            result.Sessions.Clear();
            result.SessionsCreated = 0;

            _logger.LogInformation(
                "Bulk scheduling aborted for PlanId:{PlanId}: {Conflicts} conflicts found, no sessions created",
                planId, result.Conflicts.Count);

            return result;
        }

        // Batch save
        if (sessionsToCreate.Count > 0)
        {
            await _sessionRepository.AddRangeAsync(sessionsToCreate);

            // Backfill session IDs into result
            for (int i = 0; i < sessionsToCreate.Count; i++)
            {
                result.Sessions[i].SessionId = sessionsToCreate[i].Id;
            }
        }

        result.SessionsCreated = sessionsToCreate.Count;

        _logger.LogInformation(
            "Bulk scheduling complete for PlanId:{PlanId}: {Created} sessions created, {Conflicts} conflicts",
            planId, result.SessionsCreated, result.Conflicts.Count);

        return result;
    }

    public async Task<PlanProgressSummary> GetProgressAsync(int planId)
    {
        var plan = await _planRepository.GetByIdWithLinesAsync(planId)
            ?? throw new ArgumentException($"Treatment plan {planId} not found.");

        var sessions = await _sessionRepository.GetByTreatmentPlanIdAsync(planId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return new PlanProgressSummary
        {
            PlanId = planId,
            TotalPlanned = plan.DurationWeeks * plan.WeeklyFrequency,
            SessionsCreated = sessions.Count,
            SessionsCompleted = sessions.Count(s => s.AppointmentStatusId == CompletedStatusId),
            SessionsCancelled = sessions.Count(s => s.AppointmentStatusId == CancelledStatusId),
            SessionsRemaining = sessions.Count(s => s.AppointmentStatusId != CancelledStatusId && s.AppointmentStatusId != CompletedStatusId),
            NextUpcomingDate = sessions
                .Where(s => s.SessionDate >= today && s.AppointmentStatusId != CancelledStatusId)
                .OrderBy(s => s.SessionDate)
                .Select(s => (DateOnly?)s.SessionDate)
                .FirstOrDefault(),
            Sessions = sessions.Select(s => new PlanSessionInfo
            {
                SessionId = s.Id,
                SessionDate = s.SessionDate,
                SessionTime = s.SessionTime,
                TreatmentPlanLineId = s.TreatmentPlanLineId ?? 0,
                SpecialtyAbbreviation = s.SpecialtyType?.Abbreviation ?? string.Empty,
                TherapistName = s.Therapist?.User != null
                    ? $"{s.Therapist.User.FirstName} {s.Therapist.User.LastName}".Trim()
                    : string.Empty,
                AppointmentStatusId = s.AppointmentStatusId,
                StatusName = s.AppointmentStatus?.Name ?? string.Empty,
            }).ToList()
        };
    }

    public async Task<IReadOnlyList<PlanSessionInfo>> GetSessionsAsync(int planId)
    {
        var plan = await _planRepository.GetByIdWithLinesAsync(planId)
            ?? throw new ArgumentException($"Treatment plan {planId} not found.");

        var sessions = await _sessionRepository.GetByTreatmentPlanIdAsync(planId);
        return sessions.Select(s => new PlanSessionInfo
        {
            SessionId = s.Id,
            SessionDate = s.SessionDate,
            SessionTime = s.SessionTime,
            TreatmentPlanLineId = s.TreatmentPlanLineId ?? 0,
            SpecialtyAbbreviation = s.SpecialtyType?.Abbreviation ?? string.Empty,
            TherapistName = s.Therapist?.User != null
                ? $"{s.Therapist.User.FirstName} {s.Therapist.User.LastName}".Trim()
                : string.Empty,
            AppointmentStatusId = s.AppointmentStatusId,
            StatusName = s.AppointmentStatus?.Name ?? string.Empty,
        }).ToList();
    }

    // --- Private helpers ---

    private static DateOnly ComputeTargetDate(DateOnly weekStart, byte? targetDayOfWeek)
    {
        if (targetDayOfWeek is null) return weekStart;
        // targetDayOfWeek: 1=Mon, 2=Tue, ..., 6=Sat
        int currentDow = ((int)weekStart.DayOfWeek + 6) % 7; // Mon=0..Sun=6
        int targetDow = targetDayOfWeek.Value - 1; // Mon=0..Sat=5
        int daysAhead = (targetDow - currentDow + 7) % 7;
        return weekStart.AddDays(daysAhead);
    }

    private static bool TimesOverlap(TimeOnly start1, int duration1, TimeOnly start2, int duration2)
    {
        var end1 = start1.AddMinutes(duration1);
        var end2 = start2.AddMinutes(duration2);
        return start1 < end2 && start2 < end1;
    }

    private static bool HasConflict(int therapistId, DateOnly date, TimeOnly time, int duration,
        Dictionary<int, List<(DateOnly Date, TimeOnly Start, int Duration)>> sessionMap)
    {
        if (!sessionMap.TryGetValue(therapistId, out var sessions)) return false;
        return sessions.Any(s => s.Date == date && TimesOverlap(s.Start, s.Duration, time, duration));
    }

    private static int? AutoAssignTherapist(
        int specialtyTypeId, DateOnly date, TimeOnly time, int duration,
        Dictionary<int, IReadOnlyList<int>> qualifiedBySpecialty,
        IReadOnlyList<int> patientHistory,
        Dictionary<int, List<(DateOnly Date, TimeOnly Start, int Duration)>> sessionMap)
    {
        if (!qualifiedBySpecialty.TryGetValue(specialtyTypeId, out var qualified))
            return null;

        var qualifiedSet = qualified.ToHashSet();

        // Try patient-history therapists first (ordered by most recent)
        foreach (var therapistId in patientHistory)
        {
            if (qualifiedSet.Contains(therapistId) && !HasConflict(therapistId, date, time, duration, sessionMap))
                return therapistId;
        }

        // Then remaining qualified therapists
        foreach (var therapistId in qualified)
        {
            if (!patientHistory.Contains(therapistId) && !HasConflict(therapistId, date, time, duration, sessionMap))
                return therapistId;
        }

        return null;
    }

    private static decimal ComputeProviderAmount(int therapistId, decimal netAmount, Dictionary<int, Therapist> therapistMap)
    {
        if (!therapistMap.TryGetValue(therapistId, out var therapist))
            return 0m;

        if (therapist.FeePctPerSession > 0)
            return Math.Round(netAmount * therapist.FeePctPerSession, 2);

        if (therapist.FeePerSession > 0)
            return therapist.FeePerSession;

        return 0m;
    }

    private static string GetTherapistName(int therapistId, Dictionary<int, Therapist> therapistMap)
    {
        if (!therapistMap.TryGetValue(therapistId, out var therapist) || therapist.User is null)
            return string.Empty;
        return $"{therapist.User.FirstName} {therapist.User.LastName}".Trim();
    }

    private static List<SuggestedAlternative> BuildAlternatives(
        int specialtyTypeId, DateOnly date, TimeOnly time, int duration,
        Dictionary<int, IReadOnlyList<int>> qualifiedBySpecialty,
        Dictionary<int, List<(DateOnly Date, TimeOnly Start, int Duration)>> sessionMap,
        Dictionary<int, Therapist> therapistMap)
    {
        var alternatives = new List<SuggestedAlternative>();

        if (!qualifiedBySpecialty.TryGetValue(specialtyTypeId, out var qualified))
            return alternatives;

        // Different therapist, same time
        foreach (var candidateId in qualified)
        {
            if (alternatives.Count >= 2) break;
            if (!HasConflict(candidateId, date, time, duration, sessionMap))
            {
                alternatives.Add(new SuggestedAlternative
                {
                    TherapistId = candidateId,
                    TherapistName = GetTherapistName(candidateId, therapistMap),
                    Time = time,
                    Type = "different-therapist"
                });
            }
        }

        // Different time, same qualified therapists (try 30-min offsets)
        var offsets = new[] { 30, -30, 60, -60 };
        foreach (var offset in offsets)
        {
            if (alternatives.Count >= 4) break;
            var altTime = time.AddMinutes(offset);
            foreach (var candidateId in qualified)
            {
                if (alternatives.Count >= 4) break;
                if (!HasConflict(candidateId, date, altTime, duration, sessionMap))
                {
                    alternatives.Add(new SuggestedAlternative
                    {
                        TherapistId = candidateId,
                        TherapistName = GetTherapistName(candidateId, therapistMap),
                        Time = altTime,
                        Type = "different-time"
                    });
                    break; // One alternative per time offset
                }
            }
        }

        return alternatives;
    }

    private class EffectiveLine
    {
        public int LineId { get; set; }
        public int SpecialtyTypeId { get; set; }
        public string SpecialtyAbbreviation { get; set; } = string.Empty;
        public int? TherapistId { get; set; }
        public byte? DayOfWeek { get; set; }
        public TimeOnly? Time { get; set; }
        public int Duration { get; set; }
        public decimal DiscountAmount { get; set; }
    }
}
