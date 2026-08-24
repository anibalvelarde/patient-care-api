using Microsoft.Extensions.Logging;
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

    public TreatmentPlanService(
        ILogger<TreatmentPlanService> logger,
        ITreatmentPlanRepository planRepository,
        ITherapySessionRepository sessionRepository,
        IRepository<SpecialtyType> specialtyTypeRepository)
    {
        _logger = logger;
        _planRepository = planRepository;
        _sessionRepository = sessionRepository;
        _specialtyTypeRepository = specialtyTypeRepository;
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

        // Validate lines
        if (request.Lines.Count == 0)
            throw new ArgumentException("Treatment plan must have at least one line.");

        var allSpecialties = await _specialtyTypeRepository.GetAllAsync();
        var specialtyMap = allSpecialties.ToDictionary(s => s.Id);

        foreach (var line in request.Lines)
        {
            if (!specialtyMap.TryGetValue(line.SpecialtyTypeId, out var specialty))
                throw new ArgumentException($"Specialty type {line.SpecialtyTypeId} not found.");
            if (specialty.IsDiscovery)
                throw new ArgumentException($"Treatment plan lines must use treatment specialties, not discovery type '{specialty.Abbreviation}'.");
        }

        // Map to entity
        var plan = new TreatmentPlan
        {
            PatientId = request.PatientId,
            DiscoverySessionId = request.DiscoverySessionId,
            CreatedByTherapistId = request.CreatedByTherapistId,
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
        return MapToProfile(loaded!);
    }

    public async Task<TreatmentPlanProfile?> GetByIdAsync(int id)
    {
        var plan = await _planRepository.GetByIdWithLinesAsync(id);
        return plan is null ? null : MapToProfile(plan);
    }

    public async Task<IReadOnlyList<TreatmentPlanProfile>> GetByPatientIdAsync(int patientId)
    {
        var plans = await _planRepository.GetByPatientIdAsync(patientId);
        return plans.Select(MapToProfile).ToList();
    }

    public async Task<TreatmentPlanProfile> UpdateAsync(int id, TreatmentPlanRequest request)
    {
        var plan = await _planRepository.GetByIdWithLinesAsync(id)
            ?? throw new ArgumentException($"Treatment plan {id} not found.");

        if (plan.PlanStatus != TreatmentPlan.PlanStatuses.Draft)
            throw new ArgumentException("Only draft treatment plans can be edited.");

        // Validate lines (same as create)
        if (request.Lines.Count == 0)
            throw new ArgumentException("Treatment plan must have at least one line.");

        var allSpecialties = await _specialtyTypeRepository.GetAllAsync();
        var specialtyMap = allSpecialties.ToDictionary(s => s.Id);
        foreach (var line in request.Lines)
        {
            if (!specialtyMap.TryGetValue(line.SpecialtyTypeId, out var specialty))
                throw new ArgumentException($"Specialty type {line.SpecialtyTypeId} not found.");
            if (specialty.IsDiscovery)
                throw new ArgumentException($"Treatment plan lines must use treatment specialties, not discovery type '{specialty.Abbreviation}'.");
        }

        // Update scalar fields
        plan.ResultsDocumentUrl = request.ResultsDocumentUrl;
        plan.WeeklyFrequency = request.WeeklyFrequency;
        plan.DurationWeeks = request.DurationWeeks;
        plan.Notes = request.Notes;

        // Replace lines
        plan.Lines.Clear();
        foreach (var (line, i) in request.Lines.Select((l, i) => (l, i)))
        {
            plan.Lines.Add(new TreatmentPlanLine
            {
                TreatmentPlanId = id,
                SpecialtyTypeId = line.SpecialtyTypeId,
                PreferredTherapistId = line.PreferredTherapistId,
                DayOfWeek = line.DayOfWeek,
                PreferredTime = line.PreferredTime,
                Duration = line.Duration,
                SortOrder = line.SortOrder > 0 ? line.SortOrder : (byte)i,
            });
        }

        await _planRepository.UpdateAsync(plan);
        var loaded = await _planRepository.GetByIdWithLinesAsync(id);
        return MapToProfile(loaded!);
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

        // Check for existing active plan
        var existingPlans = await _planRepository.GetByPatientIdAsync(plan.PatientId);
        var activePlan = existingPlans.FirstOrDefault(p => p.PlanStatus == TreatmentPlan.PlanStatuses.Active && p.Id != id);
        if (activePlan != null)
            throw new ArgumentException($"Patient already has an active treatment plan (Plan #{activePlan.Id}). Cancel or complete it before activating another.");

        plan.PlanStatus = TreatmentPlan.PlanStatuses.Active;
        await _planRepository.UpdateAsync(plan);
        return MapToProfile(plan);
    }

    public async Task<TreatmentPlanProfile> CompleteAsync(int id)
    {
        var plan = await _planRepository.GetByIdWithLinesAsync(id)
            ?? throw new ArgumentException($"Treatment plan {id} not found.");
        if (plan.PlanStatus != TreatmentPlan.PlanStatuses.Active)
            throw new ArgumentException($"Cannot complete a plan with status '{plan.PlanStatus}'. Only Active plans can be completed.");
        plan.PlanStatus = TreatmentPlan.PlanStatuses.Completed;
        await _planRepository.UpdateAsync(plan);
        return MapToProfile(plan);
    }

    public async Task<TreatmentPlanProfile> CancelAsync(int id)
    {
        var plan = await _planRepository.GetByIdWithLinesAsync(id)
            ?? throw new ArgumentException($"Treatment plan {id} not found.");
        if (plan.PlanStatus == TreatmentPlan.PlanStatuses.Cancelled)
            throw new ArgumentException("Plan is already cancelled.");
        plan.PlanStatus = TreatmentPlan.PlanStatuses.Cancelled;
        await _planRepository.UpdateAsync(plan);
        return MapToProfile(plan);
    }

    public async Task<IReadOnlyList<ActivePlanSummary>> GetActiveSummaryAsync()
    {
        var activePlans = await _planRepository.GetActiveWithLinesAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
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
