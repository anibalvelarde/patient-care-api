using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

public class BulkSchedulingService : IBulkSchedulingService
{
    private const int CancelledStatusId = 3;
    private const int CompletedStatusId = 4;
    private const int ProposedStatusId = 1;

    private readonly ILogger<BulkSchedulingService> _logger;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly ITherapySessionRepository _sessionRepository;
    private readonly ITherapistRepository _therapistRepository;
    private readonly ITherapistSpecialtyRepository _specialtyRepository;
    private readonly IPatientCaretakerRepository _patientCaretakerRepository;
    private readonly IPatientProfileService _patientService;
    private readonly ISpecialtyPriceRepository _priceRepository;

    public BulkSchedulingService(
        ILogger<BulkSchedulingService> logger,
        ITreatmentPlanRepository planRepository,
        ITherapySessionRepository sessionRepository,
        ITherapistRepository therapistRepository,
        ITherapistSpecialtyRepository specialtyRepository,
        IPatientCaretakerRepository patientCaretakerRepository,
        IPatientProfileService patientService,
        // WP-40 (G5): bulk derives money from the SAME price sheet as the single-session path —
        // the flat $65/hr placeholder is gone.
        ISpecialtyPriceRepository priceRepository)
    {
        _logger = logger;
        _planRepository = planRepository;
        _sessionRepository = sessionRepository;
        _therapistRepository = therapistRepository;
        _specialtyRepository = specialtyRepository;
        _patientCaretakerRepository = patientCaretakerRepository;
        _patientService = patientService;
        _priceRepository = priceRepository;
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

        // WP-23 (F10): hard block — no caretaker on file, no booking (same guard as the
        // single-session path; synthetic placeholders count). ArgumentException → 400.
        var caretakerLinks = await _patientCaretakerRepository.GetByPatientIdAsync(plan.PatientId);
        if (!caretakerLinks.Any())
            throw new ArgumentException("A caretaker must be linked to this patient before booking.");

        // WP-23 (F7): SENADIS patients get a per-line discount floor of 20% of Amount.
        // WP-37 (SEN-1/G2): expiry-aware PER SESSION DATE (shared predicate, evaluated inside
        // the loop) — a schedule spanning the expiry floors only the sessions on/before it.
        var patientProfile = await _patientService.GetByIdAsync(plan.PatientId);

        // WP-40 (G2/BK-3): booking-time discounts are DERIVED (SENADIS-only) — client line
        // discount overrides are ignored; ad-hoc discounts happen post-booking via the gated
        // Sessions.Discount.Edit path.
        if (request.LineOverrides.Any(o => o.DiscountAmount != 0m))
        {
            _logger.LogWarning(
                "WP-40: bulk line discount overrides ignored for PlanId:{PlanId} — booking discounts are derived (SENADIS-only)",
                planId);
        }

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
            };
        }).ToList();

        // WP-40 (G5): one price-sheet load feeds every line/week — same resolution rule
        // (SpecialtyPricing.Resolve at each SESSION's date) as the single-session path.
        var pricedSpecialties = (await _priceRepository.GetAllWithPricesAsync())
            .ToDictionary(s => s.Id);

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

                // WP-40 (G1): bulk creates NEW sessions, so line durations must be bookable.
                if (!SessionMoneyMath.IsBookableDuration(line.Duration))
                {
                    result.Conflicts.Add(new ScheduleConflict
                    {
                        WeekNumber = week,
                        LineId = line.LineId,
                        Date = targetDate,
                        Reason = $"Duration {line.Duration} min is not bookable — allowed: {SessionMoneyMath.BookableDurationsDisplay}.",
                    });
                    continue;
                }

                // WP-40 (G4): missing price = schedule conflict — bulk stays atomic, nothing books at $0.
                pricedSpecialties.TryGetValue(line.SpecialtyTypeId, out var pricedSpecialty);
                var price = pricedSpecialty is null
                    ? PriceResolution.None
                    : SpecialtyPricing.Resolve(pricedSpecialty, line.Duration, targetDate);
                if (price.Amount is null)
                {
                    result.Conflicts.Add(new ScheduleConflict
                    {
                        WeekNumber = week,
                        LineId = line.LineId,
                        Date = targetDate,
                        Reason = SessionMoneyMath.MissingPriceMessage(
                            pricedSpecialty?.Name ?? line.SpecialtyAbbreviation, line.Duration),
                    });
                    continue;
                }

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

                // Compute financials — WP-40 (G5): the SAME derivation as the single-session
                // path. Amount from the price sheet (resolved above at THIS session's date);
                // discount exactly-20% when SENADIS is active at the session date, else 0
                // (WP-37 expiry-aware predicate); fee on net (bulk keeps its historical 2dp
                // rounding of the fee).
                var amount = price.Amount.Value;
                var discountAmount = SessionMoneyMath.DeriveBookingDiscount(
                    amount, patientProfile?.HasActiveSenadisDiscount(targetDate) ?? false);
                var netAmount = amount - discountAmount;
                var providerAmount = therapistMap.TryGetValue(assignedTherapistId.Value, out var feeTherapist)
                    ? Math.Round(SessionMoneyMath.ProviderFee(feeTherapist.FeePerSession, feeTherapist.FeePctPerSession, netAmount), 2)
                    : 0m;
                var grossProfit = SessionMoneyMath.GrossProfit(netAmount, providerAmount, onSiteCharge: null);

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
    }
}
