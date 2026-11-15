using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

public class TreatmentPlanService : ITreatmentPlanService
{
    private readonly ILogger<TreatmentPlanService> _logger;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly ITherapySessionRepository _sessionRepository;
    private readonly IRepository<SpecialtyType> _specialtyTypeRepository;
    private readonly ISpecialtyPriceService _priceService;

    public TreatmentPlanService(
        ILogger<TreatmentPlanService> logger,
        ITreatmentPlanRepository planRepository,
        ITherapySessionRepository sessionRepository,
        IRepository<SpecialtyType> specialtyTypeRepository,
        ISpecialtyPriceService priceService)
    {
        _logger = logger;
        _planRepository = planRepository;
        _sessionRepository = sessionRepository;
        _specialtyTypeRepository = specialtyTypeRepository;
        _priceService = priceService;
    }

    public async Task<TreatmentPlanProfile> CreateAsync(TreatmentPlanRequest request)
    {
        _logger.LogInformation("Creating treatment plan for PatientId:{PatientId}", request.PatientId);

        // Validate discovery session
        var discoverySession = await _sessionRepository.GetByIdWithSpecialtyAsync(request.DiscoverySessionId);
        if (discoverySession is null)
            throw new ArgumentException($"Discovery session {request.DiscoverySessionId} not found.");
        if (discoverySession.PatientId != request.PatientId)
            throw new ArgumentException("Discovery session does not belong to the specified patient.");
        if (discoverySession.SpecialtyType is null || !discoverySession.SpecialtyType.IsDiscovery)
            throw new ArgumentException("The referenced session is not a discovery session.");
        if (discoverySession.AppointmentStatusId != SessionStatus.Completed)
            throw new ArgumentException("Discovery session must be completed before creating a treatment plan.");

        ValidateHeader(request);
        await ValidateLinesAsync(request.Lines.Select(l => (l.SpecialtyTypeId, l.Duration)).ToList());

        // Map to entity
        var plan = new TreatmentPlan
        {
            PatientId = request.PatientId,
            DiscoverySessionId = request.DiscoverySessionId,
            // WP-61 (F-8): always the discovery session's therapist — the client value is ignored
            // (the UI used to send a hardcoded 1, crediting every plan to therapist #1).
            CreatedByTherapistId = discoverySession.TherapistId,
            PlanStatus = TreatmentPlan.PlanStatuses.Draft,
            ResultsDocumentUrl = request.ResultsDocumentUrl,
            WeeklyFrequency = request.WeeklyFrequency,
            DurationWeeks = request.DurationWeeks,
            Notes = request.Notes,
            Lines = request.Lines.Select((l, i) => new TreatmentPlanLine
            {
                SpecialtyTypeId = l.SpecialtyTypeId,
                PreferredTherapistId = l.PreferredTherapistId,
                DayOfWeek = l.DayOfWeek,
                PreferredTime = l.PreferredTime,
                Duration = l.Duration,
                SortOrder = l.SortOrder > 0 ? l.SortOrder : (byte)i,
            }).ToList()
        };

        var saved = await _planRepository.AddAsync(plan);
        _logger.LogInformation("Treatment plan {PlanId} created for PatientId:{PatientId}", saved.Id, request.PatientId);

        // Reload with navigations
        var loaded = await _planRepository.GetByIdWithLinesAsync(saved.Id);
        return await MapToProfileAsync(loaded!);
    }

    public async Task<TreatmentPlanProfile?> GetByIdAsync(int id)
    {
        var plan = await _planRepository.GetByIdWithLinesAsync(id);
        return plan is null ? null : await MapToProfileAsync(plan);
    }

    public async Task<IReadOnlyList<TreatmentPlanProfile>> GetByPatientIdAsync(int patientId)
    {
        var plans = await _planRepository.GetByPatientIdAsync(patientId);
        var profiles = new List<TreatmentPlanProfile>(plans.Count);
        foreach (var plan in plans)
            profiles.Add(await MapToProfileAsync(plan));
        return profiles;
    }

    public async Task<TreatmentPlanProfile> UpdateAsync(int id, TreatmentPlanRequest request)
    {
        var plan = await _planRepository.GetByIdWithLinesAsync(id)
            ?? throw new ArgumentException($"Treatment plan {id} not found.");

        // WP-61 (ruling 1): Draft, or Active until its sessions are scheduled. Scheduling is one-shot
        // and sessions copy line data when created, so once sessions exist an edit would change nothing.
        if (!await CanEditLinesAsync(plan))
        {
            throw new ArgumentException(plan.PlanStatus == TreatmentPlan.PlanStatuses.Active
                ? "Plan already has scheduled sessions — cancel and recreate it to change it."
                : $"A {plan.PlanStatus} treatment plan cannot be edited.");
        }

        ValidateHeader(request);
        if (plan.PlanStatus == TreatmentPlan.PlanStatuses.Active && request.Lines.Count != request.WeeklyFrequency)
        {
            throw new ArgumentException(
                $"An active plan needs one line per weekly session: {request.Lines.Count} line(s) for {request.WeeklyFrequency} session(s) per week.");
        }
        await ValidateLinesAsync(request.Lines.Select(l => (l.SpecialtyTypeId, l.Duration)).ToList());

        var byId = plan.Lines.ToDictionary(l => l.Id);
        foreach (var line in request.Lines.Where(l => l.Id.HasValue))
        {
            if (!byId.ContainsKey(line.Id!.Value))
                throw new ArgumentException($"Line {line.Id} does not belong to treatment plan {id}.");
        }

        // Update scalar fields
        plan.ResultsDocumentUrl = request.ResultsDocumentUrl;
        plan.WeeklyFrequency = request.WeeklyFrequency;
        plan.DurationWeeks = request.DurationWeeks;
        plan.Notes = request.Notes;

        // WP-61: lines are updated IN PLACE by Id (they used to be cleared and re-added, minting new
        // Ids on every save). Removing the missing ones is safe: no session can reference them yet.
        var keep = request.Lines.Where(l => l.Id.HasValue).Select(l => l.Id!.Value).ToHashSet();
        foreach (var stale in plan.Lines.Where(l => !keep.Contains(l.Id)).ToList())
            plan.Lines.Remove(stale);

        foreach (var (line, i) in request.Lines.Select((l, i) => (l, i)))
        {
            var target = line.Id.HasValue ? byId[line.Id.Value] : new TreatmentPlanLine { TreatmentPlanId = id };
            target.SpecialtyTypeId = line.SpecialtyTypeId;
            target.PreferredTherapistId = line.PreferredTherapistId;
            target.DayOfWeek = line.DayOfWeek;
            target.PreferredTime = line.PreferredTime;
            target.Duration = line.Duration;
            target.SortOrder = line.SortOrder > 0 ? line.SortOrder : (byte)i;
            if (!line.Id.HasValue)
                plan.Lines.Add(target);
        }

        await _planRepository.UpdateAsync(plan);
        var loaded = await _planRepository.GetByIdWithLinesAsync(id);
        return await MapToProfileAsync(loaded!);
    }

    public async Task<TreatmentPlanProfile> ActivateAsync(int id)
    {
        var plan = await _planRepository.GetByIdWithLinesAsync(id)
            ?? throw new ArgumentException($"Treatment plan {id} not found.");
        if (plan.PlanStatus != TreatmentPlan.PlanStatuses.Draft)
            throw new ArgumentException($"Cannot activate a plan with status '{plan.PlanStatus}'. Only Draft plans can be activated.");

        // Line count must match weekly frequency
        var lineCount = plan.Lines.Count;
        if (lineCount != plan.WeeklyFrequency)
            throw new ArgumentException($"Cannot activate: line count ({lineCount}) does not match sessions per week ({plan.WeeklyFrequency}). Adjust the plan lines or weekly frequency first.");

        // WP-61: re-checked at activation — a price can be removed after the plan was saved.
        await ValidateLinesAsync(plan.Lines.Select(l => (l.SpecialtyTypeId, l.Duration)).ToList());

        // Check for existing active plan
        var existingPlans = await _planRepository.GetByPatientIdAsync(plan.PatientId);
        var activePlan = existingPlans.FirstOrDefault(p => p.PlanStatus == TreatmentPlan.PlanStatuses.Active && p.Id != id);
        if (activePlan != null)
            throw new ArgumentException($"Patient already has an active treatment plan (Plan #{activePlan.Id}). Cancel or complete it before activating another.");

        plan.PlanStatus = TreatmentPlan.PlanStatuses.Active;
        await _planRepository.UpdateAsync(plan);
        return await MapToProfileAsync(plan);
    }

    public async Task<TreatmentPlanProfile> CompleteAsync(int id)
    {
        var plan = await _planRepository.GetByIdWithLinesAsync(id)
            ?? throw new ArgumentException($"Treatment plan {id} not found.");
        if (plan.PlanStatus != TreatmentPlan.PlanStatuses.Active)
            throw new ArgumentException($"Cannot complete a plan with status '{plan.PlanStatus}'. Only Active plans can be completed.");
        plan.PlanStatus = TreatmentPlan.PlanStatuses.Completed;
        await _planRepository.UpdateAsync(plan);
        return await MapToProfileAsync(plan);
    }

    public async Task<TreatmentPlanProfile> CancelAsync(int id)
    {
        var plan = await _planRepository.GetByIdWithLinesAsync(id)
            ?? throw new ArgumentException($"Treatment plan {id} not found.");
        if (plan.PlanStatus == TreatmentPlan.PlanStatuses.Cancelled)
            throw new ArgumentException("Plan is already cancelled.");
        plan.PlanStatus = TreatmentPlan.PlanStatuses.Cancelled;
        await _planRepository.UpdateAsync(plan);
        return await MapToProfileAsync(plan);
    }

    public async Task<IReadOnlyList<ActivePlanSummary>> GetActiveSummaryAsync()
    {
        var activePlans = await _planRepository.GetActiveWithLinesAsync();
        var today = ClinicClock.Today();
        var summaries = new List<ActivePlanSummary>();

        foreach (var plan in activePlans)
        {
            var sessions = await _sessionRepository.GetByTreatmentPlanIdAsync(plan.Id);

            var sessionsCreated = sessions.Count;
            var sessionsCompleted = sessions.Count(s => s.AppointmentStatusId == SessionStatus.Completed);
            var sessionsCancelled = sessions.Count(s => s.AppointmentStatusId == SessionStatus.Cancelled);
            var sessionsRemaining = sessions.Count(s => s.AppointmentStatusId != SessionStatus.Cancelled && s.AppointmentStatusId != SessionStatus.Completed);

            summaries.Add(new ActivePlanSummary
            {
                PlanId = plan.Id,
                DisplayTitle = BuildDisplayTitle(plan),
                PatientId = plan.PatientId,
                PatientName = plan.Patient?.User != null
                    ? $"{plan.Patient.User.FirstName} {plan.Patient.User.LastName}".Trim()
                    : string.Empty,
                PlanStatus = plan.PlanStatus,
                WeeklyFrequency = plan.WeeklyFrequency,
                DurationWeeks = plan.DurationWeeks,
                TotalPlanned = plan.DurationWeeks * plan.WeeklyFrequency,
                SessionsCreated = sessionsCreated,
                SessionsCompleted = sessionsCompleted,
                SessionsRemaining = sessionsRemaining,
                NextUpcomingDate = sessions
                    .Where(s => s.SessionDate >= today && s.AppointmentStatusId != SessionStatus.Cancelled)
                    .OrderBy(s => s.SessionDate)
                    .Select(s => (DateOnly?)s.SessionDate)
                    .FirstOrDefault(),
                NeedsAttention = sessionsRemaining == 0,
            });
        }

        return summaries;
    }

    private static void ValidateHeader(TreatmentPlanRequest request)
    {
        if (request.WeeklyFrequency < 1)
            throw new ArgumentException("Sessions per week must be at least 1.");
        if (request.DurationWeeks < 1)
            throw new ArgumentException("Plan duration must be at least 1 week.");
    }

    /// <summary>
    /// WP-61 (F-7): every line must be schedulable — a treatment (not discovery) specialty, a
    /// bookable duration, and a price (duration row, else the specialty's default) as of today.
    /// Duration/price problems are reported together in one message so the UI can show them all.
    /// Scheduling still re-checks each session's own date (a future price change can bite there).
    /// </summary>
    private async Task ValidateLinesAsync(IReadOnlyList<(int SpecialtyTypeId, int Duration)> lines)
    {
        if (lines.Count == 0)
            throw new ArgumentException("Treatment plan must have at least one line.");

        var specialtyMap = (await _specialtyTypeRepository.GetAllAsync()).ToDictionary(s => s.Id);
        var today = ClinicClock.Today();
        var problems = new List<string>();
        foreach (var (specialtyTypeId, duration) in lines)
        {
            if (!specialtyMap.TryGetValue(specialtyTypeId, out var specialty))
                throw new ArgumentException($"Specialty type {specialtyTypeId} not found.");
            if (specialty.IsDiscovery)
                throw new ArgumentException($"Treatment plan lines must use treatment specialties, not discovery type '{specialty.Abbreviation}'.");

            if (!SessionMoneyMath.IsBookableDuration(duration))
            {
                problems.Add($"{specialty.Name}: duration {duration} min is not bookable — allowed: {SessionMoneyMath.BookableDurationsDisplay}.");
                continue;
            }
            var price = await _priceService.ResolvePriceAsync(specialtyTypeId, duration, today);
            if (price.Source == AmountSource.None)
                problems.Add(SessionMoneyMath.MissingPriceMessage(specialty.Name, duration));
        }

        if (problems.Count > 0)
            throw new ArgumentException(string.Join(" ", problems.Distinct()));
    }

    private async Task<bool> CanEditLinesAsync(TreatmentPlan plan) => plan.PlanStatus switch
    {
        TreatmentPlan.PlanStatuses.Draft => true,
        TreatmentPlan.PlanStatuses.Active => !await _sessionRepository.HasSessionsForPlanAsync(plan.Id),
        _ => false,
    };

    private async Task<TreatmentPlanProfile> MapToProfileAsync(TreatmentPlan plan)
    {
        var profile = MapToProfile(plan);
        profile.CanEditLines = await CanEditLinesAsync(plan);
        return profile;
    }

    private static string BuildDisplayTitle(TreatmentPlan plan)
    {
        var lastName = plan.Patient?.User?.LastName ?? "?";
        var firstInitial = plan.Patient?.User?.FirstName?.Length > 0
            ? $"{plan.Patient.User.FirstName[0]}."
            : "?";
        var dateStr = plan.CreatedTimestamp.ToString("yyyy-MM-dd");
        var freq = $"{plan.WeeklyFrequency}x/week";
        return $"TP | {lastName}, {firstInitial} | {dateStr} | {freq}";
    }

    private static TreatmentPlanProfile MapToProfile(TreatmentPlan plan)
    {
        return new TreatmentPlanProfile
        {
            Id = plan.Id,
            DisplayTitle = BuildDisplayTitle(plan),
            PatientId = plan.PatientId,
            PatientName = plan.Patient?.User != null
                ? $"{plan.Patient.User.FirstName} {plan.Patient.User.LastName}".Trim()
                : string.Empty,
            DiscoverySessionId = plan.DiscoverySessionId,
            DiscoveryDate = plan.DiscoverySession?.SessionDate ?? default,
            DiscoverySpecialty = plan.DiscoverySession?.SpecialtyType?.Abbreviation ?? string.Empty,
            CreatedByTherapistId = plan.CreatedByTherapistId,
            CreatedByTherapistName = plan.CreatedByTherapist?.User != null
                ? $"{plan.CreatedByTherapist.User.FirstName} {plan.CreatedByTherapist.User.LastName}".Trim()
                : string.Empty,
            PlanStatus = plan.PlanStatus,
            ResultsDocumentUrl = plan.ResultsDocumentUrl,
            WeeklyFrequency = plan.WeeklyFrequency,
            DurationWeeks = plan.DurationWeeks,
            Notes = plan.Notes,
            CreatedTimestamp = plan.CreatedTimestamp,
            LastUpdatedTimestamp = plan.LastUpdatedTimestamp,
            Lines = plan.Lines.OrderBy(l => l.SortOrder).Select(l => new TreatmentPlanLineProfile
            {
                Id = l.Id,
                TreatmentPlanId = l.TreatmentPlanId,
                SpecialtyTypeId = l.SpecialtyTypeId,
                SpecialtyAbbreviation = l.SpecialtyType?.Abbreviation ?? string.Empty,
                SpecialtyName = l.SpecialtyType?.Name ?? string.Empty,
                PreferredTherapistId = l.PreferredTherapistId,
                PreferredTherapistName = l.PreferredTherapist?.User != null
                    ? $"{l.PreferredTherapist.User.FirstName} {l.PreferredTherapist.User.LastName}".Trim()
                    : null,
                DayOfWeek = l.DayOfWeek,
                PreferredTime = l.PreferredTime,
                Duration = l.Duration,
                SortOrder = l.SortOrder,
            }).ToList()
        };
    }
}
